using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering;

namespace TIA_Project_v11
{
    public sealed class SimpleMainForm : Form
    {
        // ====== UI ======
        private CheckBox cbCsvLenze, cbCsvMaster, cbCsvCamera, cbCsvHik3050, cbPlcSecurity, cbMuStart, cbMuOpenAls;
        private CheckBox cbCsvConveyorLad;
        private CheckBox cbBlockExport;
        private TextBox txtCsvPath, txtProjectPath, txtCatalog, txtLog;
        private ComboBox cboPlcCpu;
        private Button btnLoadPlcCpus;

        private TextBox txtLadTemplate;
        private Button btnBrowseLadTemplate;

        // Block export
        private ComboBox cboBlockNames;
        private Button btnRefreshBlocks;
        private TextBox txtBlockExportPath;
        private Button btnBrowseBlockExport;

        // MU (start session)
        private TextBox txtServerAlias, txtMuProjectName, txtSessionName, txtSessionDir;
        private ComboBox cboMuMode, cboMasterAddressMode;

        // MU (open .als*)
        private TextBox txtMuAlsPath;
        private Button btnBrowseMuAls;

        private Button btnBrowseCsv, btnBrowseProject, btnBrowseSessionDir, btnRun;

        public SimpleMainForm()
        {
            Text = "TIA Openness Tool";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 600);
            BuildUi();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            Controls.Add(root);

            // ===== Menu =====
            // Allow multiple options to be selected concurrently (check boxes instead of radio buttons)
            var grpMenu = new GroupBox { Text = "Opcje", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var menuFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };

            // Create check boxes instead of radio buttons so multiple options can be selected
            cbCsvLenze = new CheckBox { Text = "CSV → Lenze", AutoSize = true, Checked = false };
            cbCsvMaster = new CheckBox { Text = "CSV → Master (Pepperl)", AutoSize = true, Checked = false };
            cbCsvCamera = new CheckBox { Text = "CSV → Kamera SR-X1H3HX", AutoSize = true, Checked = false };
            cbCsvHik3050 = new CheckBox { Text = "CSV → hik 3050", AutoSize = true, Checked = false };
            cbPlcSecurity = new CheckBox { Text = "PLC → Security", AutoSize = true, Checked = false };
            cbMuStart = new CheckBox { Text = "MU → Start session (server)", AutoSize = true, Checked = false };
            cbMuOpenAls = new CheckBox { Text = "MU → Open local session (.als*)", AutoSize = true, Checked = false };
            cbCsvConveyorLad = new CheckBox { Text = "CSV → Conveyor (LAD)", AutoSize = true, Checked = false };
            cbBlockExport = new CheckBox { Text = "Export blok → XML", AutoSize = true, Checked = false };

            // Hook refresh when any option is toggled
            foreach (var cb in new[] { cbCsvLenze, cbCsvMaster, cbCsvCamera, cbCsvHik3050, cbPlcSecurity, cbMuStart, cbMuOpenAls, cbCsvConveyorLad, cbBlockExport })
                cb.CheckedChanged += (s, e) => RefreshEnabled();

            menuFlow.Controls.AddRange(new Control[] { cbCsvLenze, cbCsvMaster, cbCsvCamera, cbCsvHik3050, cbPlcSecurity, cbMuStart, cbMuOpenAls, cbCsvConveyorLad, cbBlockExport });
            grpMenu.Controls.Add(menuFlow);
            root.Controls.Add(grpMenu, 0, 0);

            // ===== Params =====
            var grpParams = new GroupBox { Text = "Parametry", Dock = DockStyle.Fill, Padding = new Padding(10) };
            root.Controls.Add(grpParams, 0, 1);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 0,
                AutoScroll = true,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grpParams.Controls.Add(grid);

            // Project
            txtProjectPath = new TextBox { Dock = DockStyle.Fill };
            btnBrowseProject = new Button { Text = "Przeglądaj", Dock = DockStyle.Fill };
            btnBrowseProject.Click += (s, e) => BrowseProject();
            AddRow(grid, "Projekt (.apXX):", txtProjectPath, btnBrowseProject);

            // CSV
            txtCsvPath = new TextBox { Dock = DockStyle.Fill };
            btnBrowseCsv = new Button { Text = "Przeglądaj", Dock = DockStyle.Fill };
            btnBrowseCsv.Click += (s, e) => BrowseCsv();
            AddRow(grid, "CSV:", txtCsvPath, btnBrowseCsv);

            cboMasterAddressMode = new ComboBox { Dock = DockStyle.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
            cboMasterAddressMode.Items.AddRange(new object[]
            {
                "Adresacja sekwencyjna",
                "Adresacja blokowa: sloty +100, slot 10 od +900",
                "Adresacja blokowa: sloty co 32 bajty I i Q"
            });
            cboMasterAddressMode.SelectedIndex = 0;
            AddRow(grid, "Tryb adresacji Master:", cboMasterAddressMode, new Label());

            // LAD template (for conveyor generator)
            txtLadTemplate = new TextBox { Dock = DockStyle.Fill };
            btnBrowseLadTemplate = new Button { Text = "Przeglądaj", Dock = DockStyle.Fill };
            btnBrowseLadTemplate.Click += (s, e) => BrowseLadTemplate();
            AddRow(grid, "LAD template (.xml):", txtLadTemplate, btnBrowseLadTemplate);

            cboBlockNames = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            btnRefreshBlocks = new Button { Text = "Wczytaj", Dock = DockStyle.Fill };
            btnRefreshBlocks.Click += (s, e) => LoadBlockNames();
            AddRow(grid, "Blok do eksportu:", cboBlockNames, btnRefreshBlocks);

            txtBlockExportPath = new TextBox { Dock = DockStyle.Fill };
            btnBrowseBlockExport = new Button { Text = "Przeglądaj", Dock = DockStyle.Fill };
            btnBrowseBlockExport.Click += (s, e) => BrowseBlockExport();
            AddRow(grid, "Zapis XML:", txtBlockExportPath, btnBrowseBlockExport);

            // PLC
            txtCatalog = new TextBox { Dock = DockStyle.Fill };
            btnLoadPlcCpus = new Button { Text = "Wczytaj", Dock = DockStyle.Fill };
            btnLoadPlcCpus.Click += (s, e) => LoadPlcCpus();
            AddRow(grid, "Catalog / Order:", txtCatalog, btnLoadPlcCpus);

            cboPlcCpu = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            AddRow(grid, "Istniejący PLC:", cboPlcCpu, new Label());

            // MU open .als*
            txtMuAlsPath = new TextBox { Dock = DockStyle.Fill };
            btnBrowseMuAls = new Button { Text = "Przeglądaj", Dock = DockStyle.Fill };
            btnBrowseMuAls.Click += (s, e) => BrowseMuAls();
            AddRow(grid, "MU Local Session (.als*):", txtMuAlsPath, btnBrowseMuAls);

            // MU start session (server)
            txtServerAlias = new TextBox { Dock = DockStyle.Fill };
            AddRow(grid, "MU Server alias:", txtServerAlias, new Label());

            txtMuProjectName = new TextBox { Dock = DockStyle.Fill };
            AddRow(grid, "MU Project name:", txtMuProjectName, new Label());

            txtSessionName = new TextBox { Dock = DockStyle.Fill, Text = "OpennessSession" };
            AddRow(grid, "Session name:", txtSessionName, new Label());

            txtSessionDir = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TIA_MU_Sessions")
            };
            btnBrowseSessionDir = new Button { Text = "Przeglądaj", Dock = DockStyle.Fill };
            btnBrowseSessionDir.Click += (s, e) => BrowseFolder(txtSessionDir);
            AddRow(grid, "Session dir:", txtSessionDir, btnBrowseSessionDir);

            cboMuMode = new ComboBox { Dock = DockStyle.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            cboMuMode.Items.AddRange(new object[] { "Exclusive", "Multiuser" });
            cboMuMode.SelectedIndex = 0;
            AddRow(grid, "Session mode:", cboMuMode, new Label());

            // RUN
            btnRun = new Button { Text = "WYKONAJ", Height = 36, Dock = DockStyle.Top };
            btnRun.Click += (s, e) => RunAction();
            AddRow(grid, "", btnRun, new Label());

            // ===== Log =====
            var grpLog = new GroupBox { Text = "Log", Dock = DockStyle.Fill, Padding = new Padding(10) };
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 9f),
            };
            grpLog.Controls.Add(txtLog);
            root.Controls.Add(grpLog, 0, 2);

            RefreshEnabled();
        }

        private void AddRow(TableLayoutPanel grid, string label, Control mid, Control right)
        {
            int r = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lbl = new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 10, 6) };
            mid.Margin = new Padding(0, 6, 10, 6);
            right.Margin = new Padding(0, 6, 0, 6);

            grid.Controls.Add(lbl, 0, r);
            grid.Controls.Add(mid, 1, r);
            grid.Controls.Add(right, 2, r);
        }

        private void RefreshEnabled()
        {
            bool wantCsv = cbCsvLenze.Checked || cbCsvMaster.Checked || cbCsvCamera.Checked || cbCsvHik3050.Checked || cbCsvConveyorLad.Checked;
            bool wantPlc = cbPlcSecurity.Checked;
            bool wantMuStart = cbMuStart.Checked;
            bool wantMuAls = cbMuOpenAls.Checked;
            bool wantExport = cbBlockExport.Checked;

            // Local project path is required when importing CSV or applying PLC security.
            txtProjectPath.Enabled = wantCsv || wantPlc || wantExport;
            btnBrowseProject.Enabled = wantCsv || wantPlc || wantExport;

            // CSV file path is only relevant when any CSV option is selected
            txtCsvPath.Enabled = wantCsv;
            btnBrowseCsv.Enabled = wantCsv;
            cboMasterAddressMode.Enabled = cbCsvMaster.Checked;

            // LAD template enabled only when LAD generation is selected
            txtLadTemplate.Enabled = cbCsvConveyorLad.Checked;
            btnBrowseLadTemplate.Enabled = cbCsvConveyorLad.Checked;

            // Catalog / Order number relevant only when PLC security is selected
            txtCatalog.Enabled = wantPlc;
            btnLoadPlcCpus.Enabled = wantPlc;
            cboPlcCpu.Enabled = wantPlc;

            // MU open .als*
            txtMuAlsPath.Enabled = wantMuAls;
            btnBrowseMuAls.Enabled = wantMuAls;

            // MU start session
            txtServerAlias.Enabled = wantMuStart;
            txtMuProjectName.Enabled = wantMuStart;
            txtSessionName.Enabled = wantMuStart;
            txtSessionDir.Enabled = wantMuStart;
            btnBrowseSessionDir.Enabled = wantMuStart;
            cboMuMode.Enabled = wantMuStart;

            cboBlockNames.Enabled = wantExport;
            btnRefreshBlocks.Enabled = wantExport;
            txtBlockExportPath.Enabled = wantExport;
            btnBrowseBlockExport.Enabled = wantExport;
        }

        private void RunAction()


        {
            try
            {
                Log("Attaching to TIA...");
                OpennessLogic.OpenTIA("attach", Log);

                bool usedMu = false;
                string muMode = null;
                LocalSession muSession = null;
                // When a Multiuser session is opened or started, capture the MultiuserProject
                Siemens.Engineering.Multiuser.MultiuserProject muProject = null;
                // Capture the path to the local MU session project file (.als) for PLC security operations
                string muLocalProjectPath = null;


                // Open local MU session (.als*) if requested
                if (cbMuOpenAls.Checked)
                {
                    string als = (txtMuAlsPath.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(als) || (!File.Exists(als) && !Directory.Exists(als)))
                    {
                        Log("MU: podaj poprawną ścieżkę do pliku/folderu .als*.");
                        return;
                    }
                    Log("MU: Opening local session (.als*) ...");
                    var session = OpennessLogic.OpenMuLocalSessionFile(als, openServerProjectView: false, logger: Log);
                    if (session == null)
                    {
                        Log("MU: Open local session failed (null).");
                        return;
                    }
                    Log("MU: Local session opened.");
                    Log("INFO: W MU v19 tia.Projects może nadal być 0. To nie jest błąd narzędzia.");
                    usedMu = true;
                    muSession = session;
                    // capture project from session
                    try { muProject = muSession.Project; } catch { muProject = null; }
                    // store local MU project path (.als) for PLC security operations
                    muLocalProjectPath = als;
                }

                // Start MU session (server) if requested
                if (cbMuStart.Checked)
                {
                    string alias = (txtServerAlias.Text ?? "").Trim();
                    string proj = (txtMuProjectName.Text ?? "").Trim();
                    string sessName = (txtSessionName.Text ?? "").Trim();
                    string sessDir = (txtSessionDir.Text ?? "").Trim();
                    string mode = (cboMuMode.SelectedItem?.ToString() ?? "Exclusive");

                    if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(proj))
                    {
                        Log("MU: server alias i project name wymagane.");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(sessDir))
                    {
                        Log("MU: session dir wymagany.");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(sessName)) sessName = "OpennessSession";

                    Log($"MU: Start session ({mode})...");
                    // start MU session and capture localSessionPath for PLC security
                    string localSessionPath;
                    var session = OpennessLogic.StartMultiuserSession(alias, proj, sessName, sessDir, mode, out localSessionPath, Log);
                    if (session == null)
                    {
                        Log("MU: session start failed (null).");
                        return;
                    }
                    Log("MU: session opened.");
                    Log("INFO: W MU projekt może nie pojawić się w tia.Projects (v19). To normalne.");
                    usedMu = true;
                    muMode = mode;
                    muSession = session;
                    // capture project from session
                    try { muProject = muSession.Project; } catch { muProject = null; }
                    // store local MU project path (.als) for PLC security operations
                    muLocalProjectPath = localSessionPath;
                }



                // Determine if any CSV or PLC security operation is requested
                bool anyCsvOrPlc = cbCsvLenze.Checked || cbCsvMaster.Checked || cbCsvCamera.Checked || cbCsvHik3050.Checked || cbPlcSecurity.Checked;
                string projectPath = (txtProjectPath.Text ?? "").Trim();

                // Remove the restriction on PLC security in MU sessions

                // Determine whether a valid .apXX project path is required:
                // Only require a project path when not working in a MU session.  When running
                // PLC security in a MU session, the local MU project path (.als) will be
                // used instead, so no .apXX path is needed.
                bool needProjectPath = (!usedMu);
                if (anyCsvOrPlc && needProjectPath)
                {
                    bool projectValid = !string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath);
                    if (!projectValid)
                    {
                        if (usedMu)
                        {
                            // In MU, try to get the local session project path, but this may still not be a .ap file
                            string opened = OpennessLogic.GetOpenedProjectPath(Log);
                            if (!string.IsNullOrWhiteSpace(opened) && File.Exists(opened))
                                projectPath = opened;
                        }
                        projectValid = !string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath);
                        if (!projectValid)
                        {
                            Log("Podaj poprawną ścieżkę projektu .apXX.");
                            return;
                        }
                    }
                }

                // === CSV Lenze import ===
                if (cbCsvLenze.Checked)
                {
                    string csv = (txtCsvPath.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(csv) || !File.Exists(csv))
                    {
                        Log("Podaj poprawną ścieżkę CSV.");
                        return;
                    }

                    Log("RUN: CSV Lenze...");
                    if (muProject != null)
                        OpennessLogic.ImportDevicesFromCsv(muProject, csv, "Lenze", Log);  // <-- MultiuserProject
                    else
                        OpennessLogic.RunWithGui(projectPath, csv, "Lenze", Log);

                    Log("DONE: CSV Lenze");
                }


                if (cbCsvMaster.Checked)
                {
                    string csv = (txtCsvPath.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(csv) || !File.Exists(csv))
                    {
                        Log("Podaj poprawną ścieżkę CSV.");
                        return;
                    }

                    Log("RUN: CSV Master...");
                    string masterAddressMode = cboMasterAddressMode.SelectedIndex == 1
                        ? "BlockPerDevice"
                        : cboMasterAddressMode.SelectedIndex == 2
                            ? "BlockPerSlot32"
                            : "Sequential";
                    if (muProject != null)
                        OpennessLogic.ImportDevicesFromCsv(muProject, csv, "Master", Log, masterAddressMode); // <-- MultiuserProject
                    else
                        OpennessLogic.RunWithGui(projectPath, csv, "Master", Log, masterAddressMode);

                    Log("DONE: CSV Master");
                }

                if (cbCsvCamera.Checked)
                {
                    string csv = (txtCsvPath.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(csv) || !File.Exists(csv))
                    {
                        Log("Podaj poprawną ścieżkę CSV.");
                        return;
                    }

                    Log("RUN: CSV Kamera SR-X1H3HX...");
                    if (muProject != null)
                        OpennessLogic.ImportDevicesFromCsv(muProject, csv, "Camera", Log);
                    else
                        OpennessLogic.RunWithGui(projectPath, csv, "Camera", Log);

                    Log("DONE: CSV Kamera SR-X1H3HX");
                }


                if (cbCsvHik3050.Checked)
                {
                    string csv = (txtCsvPath.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(csv) || !File.Exists(csv))
                    {
                        Log("Podaj poprawną ścieżkę CSV.");
                        return;
                    }

                    Log("RUN: CSV hik 3050...");
                    if (muProject != null)
                        OpennessLogic.ImportDevicesFromCsv(muProject, csv, "Hik3050", Log);
                    else
                        OpennessLogic.RunWithGui(projectPath, csv, "Hik3050", Log);

                    Log("DONE: CSV hik 3050");
                }


                // === PLC security ===
                if (cbPlcSecurity.Checked)
                {
                    string catalog = (txtCatalog.Text ?? "").Trim().Replace(" ", "");
                    var selectedCpu = string.IsNullOrWhiteSpace(catalog)
                        ? cboPlcCpu.SelectedItem as OpennessLogic.CpuCandidate
                        : null;

                    if (string.IsNullOrWhiteSpace(catalog) && selectedCpu == null)
                    {
                        Log("Podaj Catalog/Order albo wybierz istniejący PLC z listy.");
                        return;
                    }
                    Log("RUN: PLC security...");
                    if (usedMu)
                    {
                        // In a MU session, call the Multiuser overload directly on the project instance
                        OpennessLogic.ApplyPlcSecurityDefaults(muProject, catalog, selectedCpu, Log);
                    }
                    else
                    {
                        // For non-MU projects, use the project path and mode as before
                        OpennessLogic.ApplyPlcSecurityDefaults(projectPath, catalog, selectedCpu, "attach", Log);
                    }
                    Log("DONE: PLC security");
                }

                // === Block export ===
                if (cbBlockExport.Checked)
                {
                    string blockName = cboBlockNames.SelectedItem?.ToString();
                    if (string.IsNullOrWhiteSpace(blockName))
                    {
                        Log("Wybierz blok do eksportu.");
                        return;
                    }

                    string exportPath = (txtBlockExportPath.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(exportPath))
                    {
                        Log("Podaj ścieżkę pliku XML dla eksportu.");
                        return;
                    }

                    Log($"RUN: Export block '{blockName}'...");
                    if (muProject != null)
                    {
                        OpennessLogic.ExportBlockToXml(muProject, blockName, exportPath, Log);
                    }
                    else
                    {
                        Project project = null;
                        if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
                        {
                            OpennessLogic.ExportBlockToXml(projectPath, blockName, exportPath, "attach", Log);
                        }
                        else
                        {
                            project = OpennessLogic.GetCurrentProject();
                            if (project == null)
                            {
                                Log("Brak otwartego projektu w TIA. Otwórz projekt albo podaj ścieżkę .apXX.");
                                return;
                            }
                            OpennessLogic.ExportBlockToXml(project, blockName, exportPath, Log);
                        }
                    }
                    Log($"DONE: Export block '{blockName}'.");
                }

                // === Conveyor LAD generation ===
                if (cbCsvConveyorLad.Checked)
                {
                    string template = (txtLadTemplate.Text ?? "").Trim();
                    string csv = (txtCsvPath.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                    {
                        Log("Podaj poprawną ścieżkę do szablonu LAD (.xml).");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(csv) || !File.Exists(csv))
                    {
                        Log("Podaj poprawną ścieżkę CSV (lista przenośników).");
                        return;
                    }
                    Log("RUN: Conveyor LAD generation...");
                    bool ok;
                    if (muProject != null)
                    {
                        ok = OpennessLogic.GenerateConveyorLad(muProject, template, csv, Log);
                    }
                    else
                    {
                        var proj = OpennessLogic.GetCurrentProject();
                        if (proj == null)
                        {
                            Log("Brak otwartego projektu w TIA. Użyj MU albo otwórz projekt .apxx w TIA przed generacją.");
                            return;
                        }
                        ok = OpennessLogic.GenerateConveyorLad(proj, template, csv, Log);
                    }
                    Log(ok ? "DONE: Conveyor LAD generation" : "FAILED: Conveyor LAD generation");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
        }

        private void BrowseProject()
        {
            using (var ofd = new OpenFileDialog { Filter = "TIA Project (*.ap*)|*.ap*|All files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                    txtProjectPath.Text = ofd.FileName;
            }
        }

        private void BrowseCsv()
        {
            using (var ofd = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                    txtCsvPath.Text = ofd.FileName;
            }
        }

        private void BrowseLadTemplate()
        {
            using (var ofd = new OpenFileDialog { Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                    txtLadTemplate.Text = ofd.FileName;
            }
        }

        private void BrowseMuAls()
        {
            // Uwaga: czasem .als19 bywa folderem. Dlatego:
            // - proponujemy dialog na plik
            // - jeśli user wskaże folder ręcznie, też przyjmujemy (textbox).
            using (var ofd = new OpenFileDialog { Filter = "MU Local Session (*.als*)|*.als*|All files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                    txtMuAlsPath.Text = ofd.FileName;
            }
        }

        private void BrowseFolder(TextBox target)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                    target.Text = fbd.SelectedPath;
            }
        }

        private void LoadBlockNames()
        {
            try
            {
                Log("Ładowanie nazw bloków...");
                OpennessLogic.OpenTIA("attach", Log);
                var names = OpennessLogic.GetBlockNames(Log);

                cboBlockNames.Items.Clear();
                foreach (var name in names)
                    cboBlockNames.Items.Add(name);

                if (cboBlockNames.Items.Count > 0)
                {
                    cboBlockNames.SelectedIndex = 0;
                    Log($"Załadowano {names.Count} bloków.");
                }
                else
                {
                    Log("Brak bloków do wyświetlenia lub brak otwartego projektu.");
                }
            }
            catch (Exception ex)
            {
                Log("LoadBlockNames EX: " + ex.Message);
            }
        }

        private void LoadPlcCpus()
        {
            try
            {
                Log("Ładowanie PLC z projektu...");
                OpennessLogic.OpenTIA("attach", Log);

                System.Collections.Generic.List<OpennessLogic.CpuCandidate> cpus;

                if (cbMuOpenAls.Checked)
                {
                    string als = (txtMuAlsPath.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(als) || (!File.Exists(als) && !Directory.Exists(als)))
                    {
                        Log("PLC: dla MU podaj poprawną ścieżkę do .als* przed wczytaniem PLC.");
                        return;
                    }

                    var session = OpennessLogic.OpenMuLocalSessionFile(als, openServerProjectView: false, logger: Log);
                    if (session == null || session.Project == null)
                    {
                        Log("PLC: nie udało się otworzyć lokalnej sesji MU.");
                        return;
                    }

                    cpus = OpennessLogic.GetCpuCandidates(session.Project, Log);
                }
                else
                {
                    string projectPath = (txtProjectPath.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                    {
                        Log("PLC: podaj poprawną ścieżkę projektu .apXX przed wczytaniem PLC.");
                        return;
                    }

                    cpus = OpennessLogic.GetCpuCandidates(projectPath, "attach", Log);
                }

                cboPlcCpu.Items.Clear();
                foreach (var cpu in cpus)
                    cboPlcCpu.Items.Add(cpu);

                if (cboPlcCpu.Items.Count > 0)
                {
                    cboPlcCpu.SelectedIndex = 0;
                    Log($"Załadowano {cboPlcCpu.Items.Count} PLC.");
                }
                else
                {
                    Log("Nie znaleziono PLC w projekcie.");
                }
            }
            catch (Exception ex)
            {
                Log("LoadPlcCpus EX: " + ex.Message);
            }
        }

        private void BrowseBlockExport()
        {
            using (var sfd = new SaveFileDialog { Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*" })
            {
                if (!string.IsNullOrWhiteSpace(txtBlockExportPath.Text))
                {
                    try
                    {
                        sfd.InitialDirectory = Path.GetDirectoryName(txtBlockExportPath.Text);
                        sfd.FileName = Path.GetFileName(txtBlockExportPath.Text);
                    }
                    catch
                    {
                    }
                }

                if (sfd.ShowDialog() == DialogResult.OK)
                    txtBlockExportPath.Text = sfd.FileName;
            }
        }

        private void Log(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            if (InvokeRequired) BeginInvoke(new Action(() => txtLog.AppendText(line + Environment.NewLine)));
            else txtLog.AppendText(line + Environment.NewLine);
        }
    }
}
