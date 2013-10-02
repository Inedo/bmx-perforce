using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Inedo.BuildMaster;
using Inedo.BuildMaster.Extensibility.Agents;
using Inedo.BuildMaster.Extensibility.Providers;
using Inedo.BuildMaster.Extensibility.Providers.SourceControl;
using Inedo.BuildMaster.Files;
using Inedo.BuildMaster.Web;

namespace Inedo.BuildMasterExtensions.Perforce
{
    /// <summary>
    /// Provides functionality for getting files, browsing folders, and applying labels in Perforce.
    /// </summary>
    [ProviderProperties(
        "Perforce",
        "Supports most versions of Perforce; requires the Perforce client (P4) to be installed.")]
    [CustomEditor(typeof(PerforceProviderEditor))]
    public sealed class PerforceProvider : SourceControlProviderBase, ILabelingProvider, IClientCommandProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PerforceProvider"/> class.
        /// </summary>
        public PerforceProvider()
        {
            this.MaskPasswordInOutput = false;
            this.UseForceSync = false;
        }

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
        /// <summary>
        /// Gets the <see cref="T:System.Char"/> used by the
        /// provider to separate directories/files in a path string.
        /// </summary>
        public override char DirectorySeparator
        {
            get { return '/'; }
        }
        /// <summary>
        /// Gets a value indicating whether commands have detailed help available.
        /// </summary>
        public bool SupportsCommandHelp
        {
            get { return true; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the password should be masked in the output
        /// </summary>
        [Persistent]
        public bool MaskPasswordInOutput { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether sync operations should use -f
        /// </summary>
        [Persistent]
        public bool UseForceSync { get; set; }

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

            var fileOps = (IFileOperationsExecuter)this.Agent;

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
        public override bool IsAvailable()
        {
            return true;
        }
        public override void ValidateConnection()
        {
            P4("depots");
        }
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

            var fileOps = (IFileOperationsExecuter)this.Agent;

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
                sourcePathWithoutDepot.Replace(this.DirectorySeparator, fileOps.GetDirectorySeparator())
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


            var startInfo = new AgentProcessStartInfo
            {
                FileName = this.ExePath,
                Arguments = argBuffer.ToString()
            };

            var process = this.Agent.GetService<IRemoteProcessExecuter<IBinaryDataProcess>>().CreateProcess(startInfo);

            this.LogDebug("Executing {0}", startInfo.FileName);
            this.LogDebug("  Arguments: {0}", argBufferToDisplay.ToString());

            var stdErr = new MemoryStream();
            process.ErrorDataReceived += (s, e) => stdErr.Write(e.Data, 0, e.Data.Length);

            var stdOut = new MemoryStream();
            process.OutputDataReceived += (s, e) => stdOut.Write(e.Data, 0, e.Data.Length);

            process.Start();
            process.WaitForExit();

            if (stdErr.Length > 0)
                throw new ConnectionException(Encoding.UTF8.GetString(stdErr.ToArray()));

            this.LogDebug("Parsing {0} bytes of data.", stdOut.Length);
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

        private static void CopyFolder(IFileOperationsExecuter fileOps, string sourcePath, string targetPath)
        {
            fileOps.ClearFolder(targetPath);

            var rootEntry = fileOps.GetDirectoryEntry(
                new GetDirectoryEntryCommand
                {
                    Path = sourcePath,
                    Recurse = true,
                    IncludeRootPath = true
                }
            ).Entry;

            var separator = fileOps.GetDirectorySeparator();

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

            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        
    }
}
