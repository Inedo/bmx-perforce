using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Inedo.Agents;
using Inedo.BuildMaster.Extensibility.Agents;
using Inedo.BuildMaster.Extensibility.Providers;
using Inedo.BuildMaster.Extensibility.Providers.SourceControl;
using Inedo.BuildMaster.Files;
using Inedo.BuildMaster.Web;
using Inedo.Serialization;

namespace Inedo.BuildMasterExtensions.Perforce
{
    [DisplayName("Perforce")]
    [Description("Supports most versions of Perforce; requires the Perforce client (P4) to be installed.")]
    [CustomEditor(typeof(PerforceProviderEditor))]
    public sealed class PerforceProvider : SourceControlProviderBase, ILabelingProvider, IClientCommandProvider
    {
        /// <summary>
        /// Gets or sets the Perforce user name (null or empty for default).
        /// </summary>
        [Persistent]
        public string UserName { get; set; }
        /// <summary>
        /// Gets or sets the Perforce password (null or empty for default).
        /// </summary>
        [Persistent]
        public string Password { get; set; }
        /// <summary>
        /// Gets or sets the Perforce client name (null or empty for default).
        /// </summary>
        [Persistent]
        public string ClientName { get; set; }
        /// <summary>
        /// Gets or sets the Perforce client root directory.
        /// </summary>
        [Persistent]
        public string ServerName { get; set; }
        /// <summary>
        /// Gets or sets the path to the P4.EXE file.
        /// </summary>
        [Persistent]
        public string ExePath { get; set; }
        public override char DirectorySeparator => '/';
        public bool SupportsCommandHelp => true;
        [Persistent]
        public bool UseForceSync { get; set; }

        private bool MaskPasswordInOutput => true;

        public override void GetLatest(string sourcePath, string targetPath)
        {
            // cleaning path
            sourcePath = (sourcePath ?? string.Empty).Trim(this.DirectorySeparator);

            // sync files specified by source path
            //   ... is recursive all files (http://www.perforce.com/perforce/doc.current/manuals/cmdref/o.fspecs.html#1040647)
            if (this.UseForceSync)
                P4("sync", "-f", "//" + sourcePath + "/...");
            else
                P4("sync", "//" + sourcePath + "/...");

            var fileOps = this.Agent.GetService<IFileOperationsExecuter>();

            var fullSourcePath = this.GetFullSourcePath(fileOps, sourcePath);

            this.LogDebug("Copying from workspace ({0}) to target ({1})", fullSourcePath, targetPath);
            CopyFolder(fileOps, fullSourcePath, targetPath);
        }
        public override DirectoryEntryInfo GetDirectoryEntryInfo(string sourcePath)
        {
            sourcePath = (sourcePath ?? string.Empty).Trim(this.DirectorySeparator);

            if (string.IsNullOrEmpty(sourcePath))
            {
                var results = P4("depots");

                var depots = new List<DirectoryEntryInfo>();

                foreach (var depot in results)
                {
                    var name = depot["name"];
                    if (!string.IsNullOrEmpty(name))
                        depots.Add(new DirectoryEntryInfo(name, name, null, null));
                }

                return new DirectoryEntryInfo(string.Empty, string.Empty, depots.ToArray(), new FileEntryInfo[0]);
            }
            else
            {
                var sourceName = sourcePath.Substring(sourcePath.LastIndexOf(this.DirectorySeparator) + 1);
                return new DirectoryEntryInfo(sourceName, sourcePath, GetDepotDirectories(sourcePath), GetDepotFiles(sourcePath));
            }
        }
        public override byte[] GetFileContents(string filePath)
        {
            if (this.UseForceSync)
                P4("sync", "-f", filePath);
            else
                P4("sync", filePath);

            var fullFilePath = Path.Combine(
                GetRootPath(),
                filePath.Replace(this.DirectorySeparator, Path.PathSeparator));
            return File.ReadAllBytes(fullFilePath);
        }
        public override bool IsAvailable() => true;
        public override void ValidateConnection() => P4("depots");
        public void ApplyLabel(string label, string sourcePath)
        {
            sourcePath = "//" + sourcePath.Trim(this.DirectorySeparator) + "/...";
            P4("tag", "-l", label, sourcePath);
        }
        public void GetLabeled(string label, string sourcePath, string targetPath)
        {
            // cleaning path
            sourcePath = (sourcePath ?? string.Empty).Trim(this.DirectorySeparator);

            // sync files specified by source path
            if (this.UseForceSync)
                P4("sync", "-f", "//" + sourcePath + "/...@" + label);
            else
                P4("sync", "//" + sourcePath + "/...@" + label);

            var fileOps = this.Agent.GetService<IFileOperationsExecuter>();

            var fullSourcePath = this.GetFullSourcePath(fileOps, sourcePath);

            this.LogDebug("Copying from workspace ({0}) to target ({1})", fullSourcePath, targetPath);
            CopyFolder(fileOps, fullSourcePath, targetPath);
        }
        public IEnumerable<ClientCommand> GetAvailableCommands()
        {
            using (var stream = typeof(PerforceProvider).Assembly.GetManifestResourceStream("Inedo.BuildMasterExtensions.Perforce.PerforceCommands.txt"))
            using (var reader = new StreamReader(stream))
            {
                var line = reader.ReadLine();
                while (line != null)
                {
                    var commandInfo = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    yield return new ClientCommand(commandInfo[0].Trim(), commandInfo[1].Trim());

                    line = reader.ReadLine();
                }
            }
        }
        public void ExecuteClientCommand(string commandName, string arguments)
        {
            var results = this.ExecuteCommandLine(
                this.ExePath,
                this.GetP4CommandLineArguments(false, false) + " \"" + commandName + "\" " + arguments,
                null
            );

            foreach (var line in results.Output)
                this.LogInformation(line);

            foreach (var line in results.Error)
                this.LogError(line);
        }
        public string GetClientCommandHelp(string commandName)
        {
            var results = this.P4("help", commandName);
            if (results.Length > 0)
            {
                string data;
                if (results[0].TryGetValue("data", out data))
                    return data;
            }

            return null;
        }
        public string GetClientCommandPreview()
        {
            return this.GetP4CommandLineArguments(false, true);
        }
        public override string ToString()
        {
            return "Provides functionality for getting files, browsing folders, and applying labels in Perforce.";
        }

        private string GetRootPath()
        {
            var results = P4("info");
            string value;
            if (results.Length > 0 && results[0].TryGetValue("clientRoot", out value))
                return value;

            throw new InvalidOperationException("clientRoot is null");
        }
        private string GetFullSourcePath(IFileOperationsExecuter fileOps, string sourcePath)
        {
            var sourcePathWithoutDepot = sourcePath;
            int firstSeparatorIndex = sourcePathWithoutDepot.IndexOf(this.DirectorySeparator);
            if (firstSeparatorIndex >= 0)
                sourcePathWithoutDepot = sourcePath.Substring(firstSeparatorIndex + 1);

            // copy files from workspace to targetPath
            var fullSourcePath = fileOps.CombinePath(
                this.GetRootPath(),
                sourcePathWithoutDepot.Replace(this.DirectorySeparator, fileOps.DirectorySeparator)
            );
            return fullSourcePath;
        }
        private DirectoryEntryInfo[] GetDepotDirectories(string path)
        {
            var results = P4("dirs", "//" + path + "/*");

            var entries = new List<DirectoryEntryInfo>();

            foreach (var dir in results)
            {
                var name = dir["dir"];
                if (!string.IsNullOrEmpty(name))
                {
                    name = name.Substring(name.LastIndexOf(this.DirectorySeparator) + 1);

                    DirectoryEntryInfo entry;

                    entry = new DirectoryEntryInfo(name, path + "/" + name, null, null);

                    entries.Add(entry);
                }
            }

            return entries.ToArray();
        }
        private FileEntryInfo[] GetDepotFiles(string path)
        {
            var results = P4("files", "//" + path + "/*");

            var entries = new List<FileEntryInfo>();

            foreach (var file in results)
            {
                string name;
                if (!file.TryGetValue("depotFile", out name)) continue;

                if (!string.IsNullOrEmpty(name))
                {
                    name = name.Substring(name.LastIndexOf(this.DirectorySeparator) + 1);
                    entries.Add(new FileEntryInfo(name, path + "/" + name));
                }
            }

            return entries.ToArray();
        }
        private Dictionary<string, string>[] P4(params string[] args)
        {
            var argBuffer = new StringBuilder(this.GetP4CommandLineArguments(true, false));
            var argBufferToDisplay = new StringBuilder(this.GetP4CommandLineArguments(true, this.MaskPasswordInOutput));

            foreach (var arg in args)
            {
                argBuffer.AppendFormat("\"{0}\" ", arg);
                argBufferToDisplay.AppendFormat("\"{0}\" ", arg);
            }


            var startInfo = new RemoteProcessStartInfo
            {
                FileName = this.ExePath,
                Arguments = argBuffer.ToString()
            };

            var process = this.Agent.GetService<IRemoteProcessExecuter>().CreateProcess(startInfo);

            this.LogDebug("Executing {0}", startInfo.FileName);
            this.LogDebug("  Arguments: {0}", argBufferToDisplay.ToString());

            var binaryOutput = this.ExecuteProcessBinary(startInfo.FileName, startInfo.Arguments);
            var stdOut = new MemoryStream(binaryOutput.Item1, false);
            var stdErr = new MemoryStream(binaryOutput.Item2, false);

            if (stdErr.Length > 0)
                throw new ConnectionException(InedoLib.UTF8Encoding.GetString(stdErr.ToArray()));

            this.LogDebug($"Parsing {stdOut.Length} bytes of data.");
            var results = new List<Dictionary<string, string>>();

            try
            {
                stdOut.Position = 0;
                var reader = new BinaryReader(stdOut);
                while (reader.BaseStream.ReadByte() == '{')
                {
                    results.Add(ReadPythonDictionary(reader));
                }
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("InvalidDataException received on data: " + Convert.ToBase64String(stdOut.ToArray()), ex);
            }
            catch (EndOfStreamException ex)
            {
                throw new EndOfStreamException("EndOfStreamException received on data: " + Convert.ToBase64String(stdOut.ToArray()), ex);
            }

            //check for errors
            foreach (var item in results)
            {
                /* http://www.perforce.com/perforce/doc.current/manuals/p4script/03_python.html#1129642
                 * 0  E_EMPTY - No error
                 * 1  E_INFO - Informational message only
                 * 2  E_WARN - Warning message only
                 * 3  E_FAILED - Command failed
                 * 4  E_FATAL - Severe error; cannot continue 
                 */

                string code, data, severity;
                if (item.TryGetValue("code", out code) && code == "error")
                {
                    data = item.TryGetValue("data", out data) ? data : "An unknown error occured.";
                    severity = item.TryGetValue("severity", out severity) ? severity : "0";

                    int severityInt = int.TryParse(severity, out severityInt) ? severityInt : 0;
                    
                    if (severityInt >= 3 /*E_FAILED*/)
                        throw new ConnectionException(data);
                }
            }

            return results.ToArray();
        }
        private string GetP4CommandLineArguments(bool pythonOutput, bool hidePassword)
        {
            var argBuffer = new StringBuilder();
            
            if (pythonOutput)
                argBuffer.Append("-G ");
            if (!string.IsNullOrEmpty(this.ClientName))
                argBuffer.AppendFormat("-c \"{0}\" ", this.ClientName);
            if (!string.IsNullOrEmpty(this.ServerName))
                argBuffer.AppendFormat("-p \"{0}\" ", this.ServerName);
            if (!string.IsNullOrEmpty(this.UserName))
                argBuffer.AppendFormat("-u \"{0}\" ", this.UserName);
            if (!string.IsNullOrEmpty(this.Password))
                argBuffer.AppendFormat("-P \"{0}\" ", hidePassword ? "XXXXXXX" : this.Password);

            return argBuffer.ToString();
        }

        private Tuple<byte[], byte[]> ExecuteProcessBinary(string fileName, string args)
        {
            var fileOps = this.Agent.GetService<IFileOperationsExecuter>();
            RemoteProcessStartInfo startInfo;
            string outFileName;
            string errFileName;
            if (fileOps.DirectorySeparator == '\\')
            {
                var remoteMethod = this.Agent.GetService<IRemoteMethodExecuter>();
                outFileName = remoteMethod.InvokeFunc(Path.GetTempFileName);
                errFileName = remoteMethod.InvokeFunc(Path.GetTempFileName);

                startInfo = new RemoteProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{fileName}\" {args} > \"{outFileName}\" 2> \"{errFileName}\"\""
                };
            }
            else
            {
                outFileName = fileOps.CombinePath("/tmp", Guid.NewGuid().ToString("N") + "_p4");
                errFileName = fileOps.CombinePath("/tmp", Guid.NewGuid().ToString("N") + "_p4");

                startInfo = new RemoteProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = $"{args} > \"{outFileName}\" 2> \"{errFileName}\""
                };
            }

            try
            {
                var result = this.ExecuteCommandLine(startInfo);

                var outputData = fileOps.ReadFileBytes(outFileName);
                var errorData = fileOps.ReadFileBytes(errFileName);

                return Tuple.Create(outputData, errorData);
            }
            finally
            {
                try
                {
                    fileOps.DeleteFiles(new[] { outFileName, errFileName });
                }
                catch
                {
                }
            }
        }

        private static void CopyFolder(IFileOperationsExecuter fileOps, string sourcePath, string targetPath)
        {
            fileOps.ClearDirectory(targetPath);

            var rootEntry = fileOps.GetDirectoryEntry(
                new GetDirectoryEntryCommand
                {
                    Path = sourcePath,
                    Recurse = true,
                    IncludeRootPath = true
                }
            ).Entry;

            var separator = fileOps.DirectorySeparator;

            int sourcePathLength = sourcePath.EndsWith("/") ? sourcePath.Length : sourcePath.Length + 1;

            var directories = rootEntry
                .Flatten()
                .Select(e => sourcePathLength < e.Path.Length ? e.Path.Substring(sourcePathLength) : e.Path)
                .OrderBy(p => p.Count(c => c == separator));

            foreach (var directory in directories)
                fileOps.CreateDirectory(fileOps.CombinePath(targetPath, directory));

            var fileNames = rootEntry
                .Flatten()
                .SelectMany(d => d.Files ?? Enumerable.Empty<FileEntryInfo>())
                .Select(e => sourcePathLength < e.Path.Length ? e.Path.Substring(sourcePathLength) : e.Path)
                .OrderBy(n => n.Count(c => c == separator))
                .ToArray();

            fileOps.FileCopyBatch(
                sourcePath,
                fileNames,
                targetPath,
                fileNames,
                true,
                true
            );
        }
        private static Dictionary<string, string> ReadPythonDictionary(BinaryReader reader)
        {
            var dict = new Dictionary<string, string>();

            int keyType = reader.BaseStream.ReadByte();

            while (keyType != '0' && keyType != -1)
            {
                // read key
                if (keyType != 's')
                    throw new InvalidDataException("Unexpected key type: " + keyType.ToString());
                var key = ReadString(reader);

                // read value
                string value = string.Empty;
                {
                    var valueType = reader.BaseStream.ReadByte();
                    if (valueType == 's')
                        value = ReadString(reader);
                    else if (valueType == 'i')
                        value = reader.ReadInt32().ToString();
                    else
                        throw new InvalidDataException("Unexpected value type: " + valueType.ToString());
                }

                dict.Add(key, value);
                keyType = reader.BaseStream.ReadByte();
            }

            return dict;
        }
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == 0)
                return string.Empty;

            return InedoLib.UTF8Encoding.GetString(reader.ReadBytes(length));
        }
    }
}
