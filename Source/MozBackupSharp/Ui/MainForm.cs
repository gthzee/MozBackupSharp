using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MozBackupSharp.Core;
using MozBackupSharp.Services;

namespace MozBackupSharp.Ui
{
    public sealed class MainForm : Form
    {
        private readonly ProfileDetector _profileDetector;
        private readonly ComboBox _operationCombo;
        private readonly ComboBox _applicationCombo;
        private readonly ListBox _profileList;
        private readonly CheckedListBox _componentsList;
        private readonly CheckBox _includeUnknownCheckBox;
        private readonly CheckBox _passwordProtectCheckBox;
        private readonly ComboBox _passwordModeCombo;
        private readonly TextBox _archiveTextBox;
        private readonly SplitContainer _middleSplitContainer;
        private readonly Button _browseArchiveButton;
        private readonly Button _refreshButton;
        private readonly Button _profilesIniButton;
        private readonly Button _customProfileButton;
        private readonly Button _startButton;
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly TextBox _logTextBox;
        private readonly Label _archiveLabel;
        private readonly Label _componentsLabel;

        private IList<DetectedApplication> _applications;
        private DateTime _lastProgressUiUpdateUtc;
        private DateTime _lastProgressLogUtc;
        private int _lastLoggedProgressPercent;

        public MainForm()
        {
            _profileDetector = new ProfileDetector();
            _applications = new List<DetectedApplication>();

            Text = "MozBackupSharp - Firefox Profile Backup";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 620);
            Size = new Size(1100, 700);
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            Controls.Add(root);

            var header = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Text = "Backup and restore Firefox, Firefox forks, Thunderbird, and SeaMonkey profiles"
            };
            root.Controls.Add(header, 0, 0);

            var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            root.Controls.Add(top, 0, 1);

            top.Controls.Add(new Label { Text = "Operation:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 8, 4) }, 0, 0);
            _operationCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 20, 4) };
            _operationCombo.Items.Add("Backup selected profile to .pcv/.zip");
            _operationCombo.Items.Add("Restore .pcv/.zip into selected profile");
            _operationCombo.SelectedIndex = 0;
            _operationCombo.SelectedIndexChanged += delegate { UpdateOperationUi(); };
            top.Controls.Add(_operationCombo, 1, 0);

            top.Controls.Add(new Label { Text = "Application:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 8, 4) }, 2, 0);
            _applicationCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 0, 4) };
            _applicationCombo.SelectedIndexChanged += delegate { BindProfiles(); };
            top.Controls.Add(_applicationCombo, 3, 0);

            _middleSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None
            };
            _middleSplitContainer.Resize += delegate { CenterProfileContentSplitter(); };
            root.Controls.Add(_middleSplitContainer, 0, 2);

            var profileGroup = new GroupBox { Text = "Profiles", Dock = DockStyle.Fill, Padding = new Padding(8) };
            _middleSplitContainer.Panel1.Controls.Add(profileGroup);
            var profileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            profileGroup.Controls.Add(profileLayout);

            _profileList = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            profileLayout.Controls.Add(_profileList, 0, 0);

            var profileButtons = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            _refreshButton = new Button { Text = "Refresh", AutoSize = true };
            _refreshButton.Click += delegate { LoadApplications(); };
            _profilesIniButton = new Button { Text = "Add profiles.ini folder...", AutoSize = true };
            _profilesIniButton.Click += delegate { AddProfilesIniFolder(); };
            _customProfileButton = new Button { Text = "Use custom profile folder...", AutoSize = true };
            _customProfileButton.Click += delegate { AddCustomProfile(); };
            profileButtons.Controls.Add(_refreshButton);
            profileButtons.Controls.Add(_profilesIniButton);
            profileButtons.Controls.Add(_customProfileButton);
            profileLayout.Controls.Add(profileButtons, 0, 1);

            var componentGroup = new GroupBox { Text = "Backup content", Dock = DockStyle.Fill, Padding = new Padding(8) };
            _middleSplitContainer.Panel2.Controls.Add(componentGroup);
            var componentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
            componentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            componentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            componentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            componentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            componentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            componentGroup.Controls.Add(componentLayout);
            _componentsLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = "Choose components to include. For a full modern profile backup, leave all checked."
            };
            componentLayout.Controls.Add(_componentsLabel, 0, 0);
            _componentsList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            componentLayout.Controls.Add(_componentsList, 0, 1);
            _includeUnknownCheckBox = new CheckBox
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Checked = true,
                Text = "Include other non-cache profile files"
            };
            componentLayout.Controls.Add(_includeUnknownCheckBox, 0, 2);

            _passwordProtectCheckBox = new CheckBox
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Checked = false,
                Text = "Protect backup with password"
            };
            _passwordProtectCheckBox.CheckedChanged += delegate { UpdatePasswordModeUi(); };
            componentLayout.Controls.Add(_passwordProtectCheckBox, 0, 3);

            _passwordModeCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false,
                Margin = new Padding(18, 2, 0, 2)
            };
            _passwordModeCombo.Items.Add("Classic ZIP / original MozBackup compatible");
            _passwordModeCombo.Items.Add("AES / MozBackupSharp protected container");
            _passwordModeCombo.SelectedIndex = 0;
            componentLayout.Controls.Add(_passwordModeCombo, 0, 4);

            var archiveLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
            archiveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            archiveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            archiveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(archiveLayout, 0, 3);

            _archiveLabel = new Label { Text = "Backup file:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 8, 4) };
            archiveLayout.Controls.Add(_archiveLabel, 0, 0);
            _archiveTextBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 8, 4) };
            archiveLayout.Controls.Add(_archiveTextBox, 1, 0);
            _browseArchiveButton = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            _browseArchiveButton.Click += delegate { BrowseArchive(); };
            archiveLayout.Controls.Add(_browseArchiveButton, 2, 0);

            var actionLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = false, Height = 48 };
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.Controls.Add(actionLayout, 0, 4);

            _startButton = new Button { Text = "Start", AutoSize = false, Size = new Size(132, 32), Margin = new Padding(0, 8, 12, 6) };
            _startButton.Click += async delegate { await StartOperationAsync(); };
            actionLayout.Controls.Add(_startButton, 0, 0);
            _statusLabel = new Label
            {
                Text = "Ready.",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0, 6, 8, 4)
            };
            actionLayout.Controls.Add(_statusLabel, 1, 0);
            _progressBar = new ProgressBar { Width = 220, Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 4) };
            actionLayout.Controls.Add(_progressBar, 2, 0);

            _logTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                WordWrap = false,
                HideSelection = false,
                Font = new Font("Consolas", 9F),
                BackColor = Color.White
            };
            root.Controls.Add(_logTextBox, 0, 5);

            PopulateComponents();
            LoadApplications();
            UpdateOperationUi();
            Shown += delegate { CenterProfileContentSplitter(); };
        }

        private bool IsBackupOperation
        {
            get { return _operationCombo.SelectedIndex == 0; }
        }

        private void CenterProfileContentSplitter()
        {
            if (_middleSplitContainer == null || _middleSplitContainer.Width <= 0)
                return;

            int centeredDistance = Math.Max(
                _middleSplitContainer.Panel1MinSize,
                (_middleSplitContainer.Width - _middleSplitContainer.SplitterWidth) / 2);

            int maxDistance = _middleSplitContainer.Width - _middleSplitContainer.SplitterWidth - _middleSplitContainer.Panel2MinSize;
            if (maxDistance > 0)
                centeredDistance = Math.Min(centeredDistance, maxDistance);

            if (centeredDistance > 0 && _middleSplitContainer.SplitterDistance != centeredDistance)
                _middleSplitContainer.SplitterDistance = centeredDistance;
        }

        private void PopulateComponents()
        {
            AddComponent("Bookmarks, browsing history, favicons", BackupComponent.BookmarksAndHistory);
            AddComponent("Saved passwords and encryption keys", BackupComponent.Passwords);
            AddComponent("Cookies", BackupComponent.Cookies);
            AddComponent("Form history", BackupComponent.FormHistory);
            AddComponent("Preferences, handlers, custom chrome", BackupComponent.Preferences);
            AddComponent("Extensions and extension data", BackupComponent.Extensions);
            AddComponent("Certificates and security databases", BackupComponent.Certificates);
            AddComponent("Thunderbird mail/news folders", BackupComponent.Mail);
            AddComponent("Thunderbird address books", BackupComponent.AddressBooks);
            AddComponent("Sessions, permissions, containers, storage", BackupComponent.OtherImportantFiles);
        }

        private void AddComponent(string text, BackupComponent component)
        {
            _componentsList.Items.Add(new ComponentItem(text, component), true);
        }

        private void LoadApplications()
        {
            _applications = _profileDetector.DetectInstalledApplications();
            _applicationCombo.Items.Clear();
            foreach (DetectedApplication app in _applications)
                _applicationCombo.Items.Add(app);

            if (_applicationCombo.Items.Count > 0)
                _applicationCombo.SelectedIndex = 0;
            else
            {
                _profileList.Items.Clear();
                Log("No Mozilla-family profiles were detected. Use 'Add profiles.ini folder...' for a fork, or 'Use custom profile folder...' to select one profile manually.");
            }
        }

        private void BindProfiles()
        {
            _profileList.Items.Clear();
            DetectedApplication app = _applicationCombo.SelectedItem as DetectedApplication;
            if (app == null)
                return;

            foreach (MozillaProfile profile in app.Profiles)
                _profileList.Items.Add(profile);
            if (_profileList.Items.Count > 0)
                _profileList.SelectedIndex = 0;
        }

        private void AddProfilesIniFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the folder that contains profiles.ini for a Firefox-family browser fork";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    DetectedApplication app = _profileDetector.CreateApplicationFromProfilesIniFolder(dialog.SelectedPath, "Firefox-family fork");
                    _applications.Add(app);
                    _applicationCombo.Items.Add(app);
                    _applicationCombo.SelectedItem = app;
                    Log("Added profiles.ini folder: " + dialog.SelectedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void AddCustomProfile()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a Firefox/Firefox-fork/Thunderbird/SeaMonkey profile folder";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                DetectedApplication app = _profileDetector.CreateCustomApplication(dialog.SelectedPath);
                _applications.Add(app);
                _applicationCombo.Items.Add(app);
                _applicationCombo.SelectedItem = app;
                Log("Added custom profile folder: " + dialog.SelectedPath);
            }
        }

        private void UpdateOperationUi()
        {
            bool backup = IsBackupOperation;
            _archiveLabel.Text = backup ? "Backup file:" : "Restore file:";
            _startButton.Text = backup ? "Start backup" : "Start restore";
            _componentsList.Enabled = backup;
            _includeUnknownCheckBox.Enabled = backup;
            _passwordProtectCheckBox.Enabled = backup;
            UpdatePasswordModeUi();
            _componentsLabel.Text = backup
                ? "Choose components to include. For a full modern profile backup, leave all checked."
                : "Components are restored from the archive. Existing files can be overwritten after confirmation.";
        }

        private void UpdatePasswordModeUi()
        {
            _passwordModeCombo.Enabled = IsBackupOperation && _passwordProtectCheckBox.Checked && (_startButton == null || _startButton.Enabled);
        }

        private void BrowseArchive()
        {
            if (IsBackupOperation)
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Create backup archive";
                    dialog.Filter = "MozBackup archives (*.pcv)|*.pcv|Zip archives (*.zip)|*.zip|All files (*.*)|*.*";
                    dialog.FileName = BuildDefaultBackupName();
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        _archiveTextBox.Text = dialog.FileName;
                }
            }
            else
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "Open backup archive";
                    dialog.Filter = "MozBackup/Zip archives (*.pcv;*.zip)|*.pcv;*.zip|All files (*.*)|*.*";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _archiveTextBox.Text = dialog.FileName;
                        TryShowManifest(dialog.FileName);
                    }
                }
            }
        }

        private string BuildDefaultBackupName()
        {
            MozillaProfile profile = _profileList.SelectedItem as MozillaProfile;
            string profileName = profile == null ? "profile" : MakeFileNameSafe(profile.Name);
            return string.Format("{0}-{1:yyyyMMdd-HHmm}.pcv", profileName, DateTime.Now);
        }

        private void TryShowManifest(string path)
        {
            try
            {
                var restoreEngine = new RestoreEngine();
                Log("Archive: " + path);
                if (restoreEngine.IsArchiveEncrypted(path))
                {
                    Log("Archive is password protected. The password will be requested when restore starts.");
                    return;
                }

                BackupManifest manifest = restoreEngine.ReadManifest(path);
                Log("Manifest: " + manifest.Tool + ", " + manifest.Application + ", profile " + manifest.ProfileName + ", created " + manifest.CreatedUtc.ToLocalTime());
            }
            catch (Exception ex)
            {
                Log("Could not read archive manifest: " + ex.Message);
            }
        }

        private async Task StartOperationAsync()
        {
            MozillaProfile profile = _profileList.SelectedItem as MozillaProfile;
            if (profile == null)
            {
                MessageBox.Show(this, "Select a profile first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string archivePath = _archiveTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                MessageBox.Show(this, "Choose an archive file first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!IsBackupOperation && !File.Exists(archivePath))
            {
                MessageBox.Show(this, "The selected archive does not exist.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string archivePassword = null;
            if (IsBackupOperation && _passwordProtectCheckBox.Checked)
            {
                using (PasswordForm passwordForm = PasswordForm.ForBackup())
                {
                    if (passwordForm.ShowDialog(this) != DialogResult.OK)
                        return;
                    archivePassword = passwordForm.Password;
                }
            }
            else if (!IsBackupOperation && new RestoreEngine().IsArchiveEncrypted(archivePath))
            {
                using (PasswordForm passwordForm = PasswordForm.ForRestore())
                {
                    if (passwordForm.ShowDialog(this) != DialogResult.OK)
                        return;
                    archivePassword = passwordForm.Password;
                }
            }

            string closeAppsMessage = "Please close the selected browser or mail application before continuing. Locked profile files may be skipped or overwritten incorrectly if the application is running.";
            if (MessageBox.Show(this, closeAppsMessage + "\r\n\r\nContinue?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            bool overwrite = true;
            if (!IsBackupOperation)
            {
                overwrite = MessageBox.Show(this, "Restore will overwrite files in the selected profile when names match. Continue?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                if (!overwrite)
                    return;
            }

            SetBusy(true);
            _progressBar.Value = 0;
            _lastProgressUiUpdateUtc = DateTime.MinValue;
            _lastProgressLogUtc = DateTime.MinValue;
            _lastLoggedProgressPercent = -1;
            var progress = new Progress<BackupProgress>(HandleProgress);

            try
            {
                if (IsBackupOperation)
                {
                    BackupComponent components = GetSelectedComponents();
                    var options = new BackupOptions
                    {
                        Profile = profile,
                        ArchivePath = archivePath,
                        Password = archivePassword,
                        UseAesEncryption = _passwordProtectCheckBox.Checked && _passwordModeCombo.SelectedIndex == 1,
                        Components = components,
                        IncludeUnknownFiles = _includeUnknownCheckBox.Checked
                    };

                    BackupManifest manifest = await Task.Run(() => new BackupEngine().CreateBackup(options, progress));
                    Log(string.Format("Backup finished: {0} file(s) written to {1}", manifest.Files.Count, archivePath));
                    MessageBox.Show(this, "Backup completed successfully.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    int count = await Task.Run(() => new RestoreEngine().RestoreArchive(archivePath, profile, overwrite, archivePassword, progress));
                    Log(string.Format("Restore finished: {0} file(s) restored into {1}", count, profile.FullPath));
                    MessageBox.Show(this, "Restore completed successfully.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                ShowOperationError(ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowOperationError(Exception ex)
        {
            string message;
            if (ex is InvalidArchivePasswordException)
            {
                message = "Invalid password or protected backup cannot be opened.";
                Log("Restore failed: " + message);
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            message = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
            Log("ERROR: " + message);
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void HandleProgress(BackupProgress progress)
        {
            if (progress == null)
                return;

            int percent = Math.Max(0, Math.Min(100, progress.Percent));
            DateTime now = DateTime.UtcNow;

            // Keep the form visually stable during large backups/restores. File-level
            // progress can arrive hundreds/thousands of times; updating labels/logs
            // for every file causes flicker and layout churn on slower machines.
            bool forceUi = percent == 0 || percent == 100 ||
                           (now - _lastProgressUiUpdateUtc).TotalMilliseconds >= 150 ||
                           percent != _progressBar.Value;
            if (forceUi)
            {
                _statusLabel.Text = progress.Message ?? string.Empty;
                _progressBar.Value = percent;
                _lastProgressUiUpdateUtc = now;
            }

            bool forceLog = percent == 0 || percent == 100 ||
                            Math.Abs(percent - _lastLoggedProgressPercent) >= 5 ||
                            (now - _lastProgressLogUtc).TotalMilliseconds >= 1000;
            if (forceLog)
            {
                Log(progress.Message);
                _lastLoggedProgressPercent = percent;
                _lastProgressLogUtc = now;
            }
        }

        private BackupComponent GetSelectedComponents()
        {
            BackupComponent result = BackupComponent.None;
            foreach (object item in _componentsList.CheckedItems)
            {
                var component = item as ComponentItem;
                if (component != null)
                    result |= component.Component;
            }
            return result;
        }

        private void SetBusy(bool busy)
        {
            SuspendLayout();
            try
            {
                _operationCombo.Enabled = !busy;
                _applicationCombo.Enabled = !busy;
                _profileList.Enabled = !busy;
                _componentsList.Enabled = !busy && IsBackupOperation;
                _includeUnknownCheckBox.Enabled = !busy && IsBackupOperation;
                _passwordProtectCheckBox.Enabled = !busy && IsBackupOperation;
                _passwordModeCombo.Enabled = !busy && IsBackupOperation && _passwordProtectCheckBox.Checked;
                _archiveTextBox.Enabled = !busy;
                _browseArchiveButton.Enabled = !busy;
                _refreshButton.Enabled = !busy;
                _profilesIniButton.Enabled = !busy;
                _customProfileButton.Enabled = !busy;
                _startButton.Enabled = !busy;
                UseWaitCursor = busy;
                Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            _logTextBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private static string MakeFileNameSafe(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        private sealed class ComponentItem
        {
            public ComponentItem(string text, BackupComponent component)
            {
                Text = text;
                Component = component;
            }

            public string Text { get; private set; }
            public BackupComponent Component { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
