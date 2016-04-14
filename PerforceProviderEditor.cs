using System.Web.UI.WebControls;
using Inedo.BuildMaster.Extensibility.Providers;
using Inedo.BuildMaster.Web.Controls;
using Inedo.BuildMaster.Web.Controls.Extensions;
using Inedo.Web.Controls;

namespace Inedo.BuildMasterExtensions.Perforce
{
    internal sealed class PerforceProviderEditor : ProviderEditorBase
    {
        private ValidatingTextBox txtUserName;
        private PasswordTextBox txtPassword;
        private ValidatingTextBox txtClient;
        private ValidatingTextBox txtServer;
        private SourceControlFileFolderPicker txtP4ExecutablePath;
        private CheckBox chkUseForceSync;

        public override void BindToForm(ProviderBase extension)
        {
            var ext = (PerforceProvider)extension;
            this.txtUserName.Text = ext.UserName ?? string.Empty;
            this.txtPassword.Text = ext.Password ?? string.Empty;
            this.txtClient.Text = ext.ClientName ?? string.Empty;
            this.txtServer.Text = ext.ServerName ?? string.Empty;
            this.txtP4ExecutablePath.Text = ext.ExePath ?? string.Empty;
            this.chkUseForceSync.Checked = ext.UseForceSync;
        }
        public override ProviderBase CreateFromForm()
        {
            return new PerforceProvider
            {
                UserName = this.txtUserName.Text,
                Password = this.txtPassword.Text,
                ClientName = this.txtClient.Text,
                ServerName = this.txtServer.Text,
                ExePath = this.txtP4ExecutablePath.Text,
                UseForceSync = this.chkUseForceSync.Checked
            };
        }

        protected override void CreateChildControls()
        {
            this.txtUserName = new ValidatingTextBox();
            this.txtPassword = new PasswordTextBox();
            this.txtClient = new ValidatingTextBox();
            this.txtServer = new ValidatingTextBox();
            this.txtP4ExecutablePath = new SourceControlFileFolderPicker { ServerId = this.EditorContext.ServerId };
            this.chkUseForceSync = new CheckBox { Text = "Use Force Sync" };

            this.Controls.Add(
                new SlimFormField("P4 executable:", this.txtP4ExecutablePath),
                new SlimFormField("User name:", this.txtUserName),
                new SlimFormField("Password:", this.txtPassword),
                new SlimFormField("Workspace (client) name:", this.txtClient),
                new SlimFormField("Perforce server:", this.txtServer),
                new SlimFormField("Additional options:", this.chkUseForceSync)
                {
                    HelpText = "By default, the P4 client will manage the files stored in the local workspace. Selecting the Force option will append a -f to the sync operation, forcing a refresh of the workspace and possibly taking longer to download files."
                }
            );
        }
    }
}
