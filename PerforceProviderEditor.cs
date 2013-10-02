using System.Web.UI.WebControls;
using Inedo.BuildMaster.Extensibility.Providers;
using Inedo.BuildMaster.Web.Controls;
using Inedo.BuildMaster.Web.Controls.Extensions;
using Inedo.Web.Controls;

namespace Inedo.BuildMasterExtensions.Perforce
{
    internal sealed class PerforceProviderEditor : ProviderEditorBase
    {
        private TextBox txtUserName;
        private PasswordTextBox txtPassword;
        private TextBox txtClient;
        private TextBox txtServer;
        private SourceControlFileFolderPicker txtP4ExecutablePath;
        private CheckBox chkUseForceSync;
        private CheckBox chkMaskPasswordInOutput;

        /// <summary>
        /// Initializes a new instance of the <see cref="PerforceProviderEditor"/> class.
        /// </summary>
        public PerforceProviderEditor()
        {
        }

        protected override void CreateChildControls()
        {
            this.txtUserName = new TextBox() { ID = "txtUserName" };
            this.txtPassword = new PasswordTextBox() { ID = "txtPassword" };
            this.txtClient = new TextBox() { ID = "txtClient" };
            this.txtServer = new TextBox() { ID = "txtServer" };
            this.txtP4ExecutablePath = new SourceControlFileFolderPicker() { ServerId = this.EditorContext.ServerId };
            this.chkUseForceSync = new CheckBox { Text = "Use Force Sync" };
            this.chkMaskPasswordInOutput = new CheckBox { Text = "Mask password in output" };

            CUtil.Add(this,
                new FormFieldGroup("Perforce Executable Path",
                    "The path to the P4 executable on the server.",
                    false,
                    new StandardFormField("P4 Executable:", txtP4ExecutablePath)),
                new FormFieldGroup("Client Options",
                    "Allows individual client defaults to be overridden. If a value is not provided, the client default will be used.<br/><br/>Note that a Workspace must be created on the target server first.",
                    false,
                    new StandardFormField("User Name:", txtUserName),
                    new StandardFormField("Password:", txtPassword),
                    new StandardFormField("", this.chkMaskPasswordInOutput),
                    new StandardFormField("Workspace (Client) Name:", txtClient),
                    new StandardFormField("Perforce Server:", txtServer)
                ),
                new FormFieldGroup("Additional Options",
                    "By default, the P4 client will manage the files stored in the local workspace. Selecting the Force option will append a -f to the sync operation, forcing a refresh of the workspace and possibly taking longer to download files.",
                    false,
                    new StandardFormField("", this.chkUseForceSync))
            );
        }

        public override void BindToForm(ProviderBase extension)
        {
            EnsureChildControls();

            var ext = (PerforceProvider)extension;
            this.txtUserName.Text = ext.UserName ?? string.Empty;
            this.txtPassword.Text = ext.Password ?? string.Empty;
            this.txtClient.Text = ext.ClientName ?? string.Empty;
            this.txtServer.Text = ext.ServerName ?? string.Empty;
            this.txtP4ExecutablePath.Text = ext.ExePath ?? string.Empty;
            this.chkMaskPasswordInOutput.Checked = ext.MaskPasswordInOutput;
            this.chkUseForceSync.Checked = ext.UseForceSync;
        }

        public override ProviderBase CreateFromForm()
        {
            EnsureChildControls();

            return new PerforceProvider()
            {
                UserName = this.txtUserName.Text,
                Password = this.txtPassword.Text,
                ClientName = this.txtClient.Text,
                ServerName = this.txtServer.Text,
                ExePath = this.txtP4ExecutablePath.Text,
                MaskPasswordInOutput = chkMaskPasswordInOutput.Checked,
                UseForceSync = this.chkUseForceSync.Checked

            };
        }
    }
}
