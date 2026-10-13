using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library.MasterCopies;
using System.Reflection;
using Siemens.Engineering.Security;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System.Security;


namespace TIA_Project_v11
{
    public static class OpennessLogic
    {
        public class LenzeDevice { public string Name { get; set; } public string DeviceName { get; set; } public string Type { get; set; } public string IpAddress { get; set; } public string SubnetName { get; set; } public int ProfinetNumber { get; set; } public string PlcName { get; set; } public string GroupName { get; set; } public int StartAddress { get; set; } }
        public class PepperlDevice { public string Name { get; set; } public string DeviceName { get; set; } public string Type { get; set; } public string IpAddress { get; set; } public string SubnetName { get; set; } public int ProfinetNumber { get; set; } public string PlcName { get; set; } public string GroupName { get; set; } public int StartAddress { get; set; } public string Slot1Name { get; set; } public string Slot1Type { get; set; } public string Slot2Name { get; set; } public string Slot2Type { get; set; } public string Slot3Name { get; set; } public string Slot3Type { get; set; } public string Slot4Name { get; set; } public string Slot4Type { get; set; } public string Slot5Name { get; set; } public string Slot5Type { get; set; } public string Slot6Name { get; set; } public string Slot6Type { get; set; } public string Slot7Name { get; set; } public string Slot7Type { get; set; } public string Slot8Name { get; set; } public string Slot8Type { get; set; } public string Slot9Name { get; set; } public string Slot9Type { get; set; } public string Slot10Name { get; set; } public string Slot10Type { get; set; } }
        public class KeyenceCameraDevice { public string Name { get; set; } public string DeviceName { get; set; } public string Type { get; set; } public string IpAddress { get; set; } public string SubnetName { get; set; } public int ProfinetNumber { get; set; } public string PlcName { get; set; } public string GroupName { get; set; } public int StartAddress { get; set; } public int InputStartAddress { get; set; } public int OutputStartAddress { get; set; } }

        private const string PepperlGsdType = "GSD:GSDML-V2.35-PEPPERL+FUCHS-ICE38IOL-AMS-20250201.XML";
        private const string PepperlMasterType = PepperlGsdType + "/DAP/DAP4";
        private const string PepperlEmptyModuleType = PepperlGsdType + "/M/ID_Mod_IOLinkEmpty";
        private const string PepperlG20ModuleType = PepperlGsdType + "/M/ID_Mod_G20_984229";
        private const string KeyenceCameraType = "GSD:GSDML-V2.43-KEYENCE-SR-X1H3H-20240716.XML/DAP/DAP1";

        private enum PepperlAddressMode
        {
            Sequential,
            BlockPerDevice,
            BlockPerSlot32
        }

        private enum ModuleIoDirection
        {
            None,
            Input,
            Output,
            InputOutput
        }

        private sealed class PepperlAddressingSettings
        {
            public PepperlAddressMode Mode { get; set; }
        }

        private sealed class PepperlAddressingState
        {
        }

        private sealed class ModuleAddressingProfile
        {
            public ModuleIoDirection Direction { get; set; }
            public int InputSize { get; set; }
            public int OutputSize { get; set; }

            public int MaxSize
            {
                get { return Math.Max(InputSize, OutputSize); }
            }
        }

        private static TiaPortal tia;

        /// <summary>
        /// Returns the first open project in the attached TIA instance, if any.  If no
        /// project is open or TIA is not attached, returns null.
        /// </summary>
        public static Project GetCurrentProject()
        {
            try
            {
                return tia?.Projects?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // ================= LAD Conveyor Generation =================
        /// <summary>
        /// Finds the first available PlcSoftware within a traditional Project.  This helper
        /// iterates through all devices and returns the first PlcSoftware encountered.  If
        /// none is found, returns null.
        /// </summary>
        private static Siemens.Engineering.SW.PlcSoftware FindFirstPlcSoftware(Project project, Action<string> logger)
        {
            if (project == null) return null;
            foreach (var device in project.Devices)
            {
                foreach (var di in device.DeviceItems)
                {
                    var swc = di.GetService<SoftwareContainer>();
                    if (swc != null)
                    {
                        var plc = swc.Software as Siemens.Engineering.SW.PlcSoftware;
                        if (plc != null)
                        {
                            logger?.Invoke($"Found PlcSoftware on device {device.Name} ({di.Name}).");
                            return plc;
                        }
                    }
                }
            }
            logger?.Invoke("No PlcSoftware found in project.");
            return null;
        }

        /// <summary>
        /// Finds the first available PlcSoftware within a MultiuserProject.  Iterates over
        /// devices to locate a CPU and returns its PlcSoftware.  If none is found, returns
        /// null.
        /// </summary>
        private static Siemens.Engineering.SW.PlcSoftware FindFirstPlcSoftware(Siemens.Engineering.Multiuser.MultiuserProject project, Action<string> logger)
        {
            if (project == null) return null;
            foreach (var device in project.Devices)
            {
                foreach (var di in device.DeviceItems)
                {
                    var swc = di.GetService<SoftwareContainer>();
                    if (swc != null)
                    {
                        var plc = swc.Software as Siemens.Engineering.SW.PlcSoftware;
                        if (plc != null)
                        {
                            logger?.Invoke($"Found PlcSoftware on device {device.Name} ({di.Name}).");
                            return plc;
                        }
                    }
                }
            }
            logger?.Invoke("No PlcSoftware found in multiuser project.");
            return null;
        }

        /// <summary>
        /// Generates conveyor LAD networks from a template and imports the resulting block
        /// into the first PLC found in a traditional project.  The CSV defines the
        /// conveyors and their order.  The template must be a ladder XML exported from
        /// TIA Portal containing one network and static member as described in
        /// LadConveyorGenerator.  This method writes a temporary XML in the session
        /// directory, imports it and compiles the PLC software.
        /// </summary>
        public static bool GenerateConveyorLad(Project project, string templateXmlPath, string csvPath, Action<string> logger)
        {
            try
            {
                var plc = FindFirstPlcSoftware(project, logger);
                if (plc == null)
                    return false;
                if (!File.Exists(templateXmlPath))
                {
                    logger?.Invoke("Template XML file not found: " + templateXmlPath);
                    return false;
                }
                if (!File.Exists(csvPath))
                {
                    logger?.Invoke("CSV file not found: " + csvPath);
                    return false;
                }
                // create temp file in user's temp directory
                string tempFile = Path.Combine(Path.GetTempPath(), $"lad_conv_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
                Autogen.LadConveyorGenerator.GenerateXmlFromTemplate(templateXmlPath, csvPath, tempFile, logger);
                // import and compile using helper functions
                var blocks = plc.BlockGroup.Blocks;
                var fileInfo = new FileInfo(tempFile);
                // Use helper to import block via best available Import overload
                bool imported = TryImportBlocks(blocks, fileInfo, logger);
                logger?.Invoke(imported ? "Import OK." : "Import FAILED.");
                // Compile via ICompilable if available, otherwise fallback
                TryCompilePlc(plc, logger);
                logger?.Invoke("Conveyor LAD import complete.");
                return imported;
            }
            catch (Exception ex)
            {
                logger?.Invoke("GenerateConveyorLad (project) EX: " + ex.Message);
                return false;
            }
        }

       
        public static bool GenerateConveyorLad(Siemens.Engineering.Multiuser.MultiuserProject project, string templateXmlPath, string csvPath, Action<string> logger)
        {
            try
            {
                var plc = FindFirstPlcSoftware(project, logger);
                if (plc == null)
                    return false;
                if (!File.Exists(templateXmlPath))
                {
                    logger?.Invoke("Template XML file not found: " + templateXmlPath);
                    return false;
                }
                if (!File.Exists(csvPath))
                {
                    logger?.Invoke("CSV file not found: " + csvPath);
                    return false;
                }
                string tempFile = Path.Combine(Path.GetTempPath(), $"lad_conv_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
                Autogen.LadConveyorGenerator.GenerateXmlFromTemplate(templateXmlPath, csvPath, tempFile, logger);
                // import and compile using helper functions
                var blocks = plc.BlockGroup.Blocks;
                var fileInfo = new FileInfo(tempFile);
                bool imported = TryImportBlocks(blocks, fileInfo, logger);
                logger?.Invoke(imported ? "Import OK." : "Import FAILED.");
                TryCompilePlc(plc, logger);
                logger?.Invoke("Conveyor LAD import complete.");
                return imported;
            }
            catch (Exception ex)
            {
                logger?.Invoke("GenerateConveyorLad (MU) EX: " + ex.Message);
                return false;
            }
        }

        // ================= Block XML export =================
        public static List<string> GetBlockNames(Project project, Action<string> logger)
        {
            var names = new List<string>();
            try
            {
                var plc = FindFirstPlcSoftware(project, logger);
                if (plc == null)
                    return names;

                foreach (var block in plc.BlockGroup.Blocks)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(block.Name))
                            names.Add(block.Name);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("GetBlockNames EX: " + ex.Message);
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> GetBlockNames(Siemens.Engineering.Multiuser.MultiuserProject project, Action<string> logger)
        {
            var names = new List<string>();
            try
            {
                var plc = FindFirstPlcSoftware(project, logger);
                if (plc == null)
                    return names;

                foreach (var block in plc.BlockGroup.Blocks)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(block.Name))
                            names.Add(block.Name);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("GetBlockNames (MU) EX: " + ex.Message);
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> GetBlockNames(Action<string> logger)
        {
            try
            {
                if (tia == null)
                {
                    logger?.Invoke("GetBlockNames: TIA not initialized.");
                    return new List<string>();
                }

                try
                {
                    if (tia.LocalSessions.Count > 0)
                    {
                        var project = tia.LocalSessions[0].Project;
                        if (project != null)
                            return GetBlockNames(project, logger);
                    }
                }
                catch
                {
                }

                var currentProject = GetCurrentProject();
                if (currentProject != null)
                    return GetBlockNames(currentProject, logger);
            }
            catch (Exception ex)
            {
                logger?.Invoke("GetBlockNames EX: " + ex.Message);
            }

            return new List<string>();
        }

        public static bool ExportBlockToXml(Project project, string blockName, string exportPath, Action<string> logger)
        {
            try
            {
                if (project == null)
                {
                    logger?.Invoke("ExportBlockToXml: No project provided.");
                    return false;
                }

                var plc = FindFirstPlcSoftware(project, logger);
                if (plc == null)
                    return false;

                var block = plc.BlockGroup.Blocks.Find(blockName);
                if (block == null)
                {
                    logger?.Invoke("ExportBlockToXml: Block not found: " + blockName);
                    return false;
                }

                var fileInfo = PrepareExportFile(exportPath);
                block.Export(fileInfo, ExportOptions.WithDefaults);
                logger?.Invoke($"ExportBlockToXml: Block '{blockName}' exported to '{fileInfo.FullName}'.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Invoke("ExportBlockToXml EX: " + ex.Message);
                return false;
            }
        }

        public static bool ExportBlockToXml(Siemens.Engineering.Multiuser.MultiuserProject project, string blockName, string exportPath, Action<string> logger)
        {
            try
            {
                if (project == null)
                {
                    logger?.Invoke("ExportBlockToXml (MU): No multiuser project provided.");
                    return false;
                }

                var plc = FindFirstPlcSoftware(project, logger);
                if (plc == null)
                    return false;

                var block = plc.BlockGroup.Blocks.Find(blockName);
                if (block == null)
                {
                    logger?.Invoke("ExportBlockToXml (MU): Block not found: " + blockName);
                    return false;
                }

                var fileInfo = PrepareExportFile(exportPath);
                block.Export(fileInfo, ExportOptions.WithDefaults);
                logger?.Invoke($"ExportBlockToXml (MU): Block '{blockName}' exported to '{fileInfo.FullName}'.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Invoke("ExportBlockToXml (MU) EX: " + ex.Message);
                return false;
            }
        }

        public static bool ExportBlockToXml(string projectPath, string blockName, string exportPath, string mode, Action<string> logger)
        {
            if (tia == null)
                OpenTIA(mode, logger);
            if (tia == null)
                throw new InvalidOperationException("TIA is not available.");

            var project = GetOrOpenProject(projectPath, logger);
            return ExportBlockToXml(project, blockName, exportPath, logger);
        }

        private static FileInfo PrepareExportFile(string exportPath)
        {
            if (string.IsNullOrWhiteSpace(exportPath))
                throw new ArgumentException("Export path is empty.", nameof(exportPath));

            var fileInfo = new FileInfo(exportPath);
            if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                fileInfo.Directory.Create();

            return fileInfo;
        }

        // ================= PLC selection / security helpers =================
        public sealed class CpuCandidate
        {
            public string DeviceName { get; set; }
            public string CpuName { get; set; }
            public string OrderNumber { get; set; }
            public string FirmwareVersion { get; set; }
            public string TypeIdentifierNormalized { get; set; }

            public override string ToString()
            {
                string fw = string.IsNullOrWhiteSpace(FirmwareVersion) ? "" : $" {FirmwareVersion}";
                string ord = string.IsNullOrWhiteSpace(OrderNumber) ? "" : $"{OrderNumber}";
                return $"{CpuName} ({ord}{fw}) @ {DeviceName}";
            }
        }


        public static void OpenTIA(string mode, Action<string> logger)
        {
            mode = (mode ?? "").Trim().ToLowerInvariant();

            if (mode == "attach")
            {
                var procs = TiaPortal.GetProcesses().ToList();
                logger?.Invoke($"TIA processes: {procs.Count}");
                if (procs.Count == 0)
                    throw new InvalidOperationException("No running TIA Portal instance found to attach.");

                // attach to first (albo możesz rozszerzyć o wybór)
                tia = procs.First().Attach();
                logger?.Invoke("Attached to existing TIA.");
                return;
            }

            tia = new TiaPortal(TiaPortalMode.WithUserInterface);
            logger?.Invoke("Started new TIA.");
        }



        public static LocalSession StartExclusiveSession(
    string serverAlias,
    string projectName,
    string sessionName,
    string sessionDirectory,
    Action<string> logger)
        {
            try
            {
                if (tia == null)
                {
                    logger("ERROR: TIA not initialized.");
                    return null;
                }

                // 1. Pobranie serwera MU
                ProjectServer server = tia.ProjectServers
                    .FirstOrDefault(s => s.ServerName.Equals(serverAlias, StringComparison.OrdinalIgnoreCase));

                if (server == null)
                {
                    logger("ERROR: MU Server not found: " + serverAlias);
                    return null;
                }

                logger("MU server connected.");

                // 2. Pobranie listy projektów MU
                var serverProjects = server.GetServerProjects();
                var projectInfo = serverProjects
                    .FirstOrDefault(p => p.ProjectName.Equals(projectName, StringComparison.OrdinalIgnoreCase));

                if (projectInfo == null)
                {
                    logger("ERROR: Project not found on MU server.");
                    return null;
                }

                logger("Server project found: " + projectInfo.ProjectName);

                // 3. Create exclusive local session
                DirectoryInfo dir = new DirectoryInfo(sessionDirectory);
                if (!dir.Exists) dir.Create();

                logger("Creating EXCLUSIVE local session...");

                LocalSessionInfo localInfo = server.CreateLocalSession(
                    projectInfo,
                    sessionName,
                    dir,
                    SessionCreationMode.Exclusive   // ✔ POPRAWNY ENUM
                );

                logger("Local session created.");
                logger(" → " + localInfo.ProjectFileInfo.FullName);

                // 4. Open exclusive local session
                logger("Opening exclusive session...");

                LocalSession session =
                    tia.LocalSessions.Open(localInfo.ProjectFileInfo);

                logger("Exclusive session opened.");

                return session;
            }
            catch (Exception ex)
            {
                logger("StartExclusiveSession EX: " + ex.Message);
                return null;
            }
        }
        public static List<string> GetServerProjectNames(string serverAlias, Action<string> logger)
        {
            var result = new List<string>();

            try
            {
                if (tia == null)
                {
                    logger("ERROR: TIA not initialized.");
                    return result;
                }

                ProjectServer server = tia.ProjectServers
                    .FirstOrDefault(s => s.ServerName.Equals(serverAlias, StringComparison.OrdinalIgnoreCase));

                if (server == null)
                {
                    logger("ERROR: MU Server not found: " + serverAlias);
                    return result;
                }

                logger("Reading project list from server...");

                var projects = server.GetServerProjects();

                foreach (var p in projects)
                {
                    result.Add(p.ProjectName);
                }

                logger($"Found {result.Count} project(s) on server.");

                return result;
            }
            catch (Exception ex)
            {
                logger("GetServerProjectNames EX: " + ex.Message);
                return result;
            }
        }

        public static LocalSession StartMultiuserSession(
    string serverAlias,
    string projectName,
    string sessionName,
    string sessionDirectory,
    string mode, // "Exclusive" | "Multiuser"
    Action<string> logger)
        {
            try
            {
                if (tia == null)
                {
                    logger("ERROR: TIA not initialized.");
                    return null;
                }

                ProjectServer server = tia.ProjectServers
                    .FirstOrDefault(s => s.ServerName.Equals(serverAlias, StringComparison.OrdinalIgnoreCase));

                if (server == null)
                {
                    logger("ERROR: MU server not found: " + serverAlias);
                    return null;
                }

                logger("Connected to MU server.");

                var projects = server.GetServerProjects();
                var projectInfo = projects
                    .FirstOrDefault(p => p.ProjectName.Equals(projectName, StringComparison.OrdinalIgnoreCase));

                if (projectInfo == null)
                {
                    logger("ERROR: Project not found on server.");
                    return null;
                }

                DirectoryInfo dir = new DirectoryInfo(sessionDirectory);
                if (!dir.Exists) dir.Create();

                SessionCreationMode creationMode =
                    mode == "Multiuser"
                        ? SessionCreationMode.Multiuser
                        : SessionCreationMode.Exclusive;

                logger("Creating session: " + creationMode);

                LocalSessionInfo localInfo = server.CreateLocalSession(
                    projectInfo,
                    sessionName,
                    dir,
                    creationMode
                );

                logger("Opening local session...");

                LocalSession session =
                    tia.LocalSessions.Open(localInfo.ProjectFileInfo);

                logger("Session opened: " + localInfo.ProjectFileInfo.FullName);

                return session;
            }
            catch (Exception ex)
            {
                logger("StartMultiuserSession EX: " + ex.Message);
                return null;
            }
        }
        public static LocalSession StartMultiuserSession(
    string serverAlias,
    string projectName,
    string sessionName,
    string sessionDirectory,
    string mode, // "Exclusive" | "Multiuser"
    out string localSessionProjectPath,
    Action<string> logger)
        {
            localSessionProjectPath = null;

            try
            {
                if (tia == null)
                    throw new InvalidOperationException("TIA not initialized. Call OpenTIA first.");

                ProjectServer server = tia.ProjectServers
                    .FirstOrDefault(s => s.ServerName.Equals(serverAlias, StringComparison.OrdinalIgnoreCase));

                if (server == null)
                    throw new InvalidOperationException("MU server not found: " + serverAlias);

                logger?.Invoke("Connected to MU server.");

                var projects = server.GetServerProjects();
                var projectInfo = projects
                    .FirstOrDefault(p => p.ProjectName.Equals(projectName, StringComparison.OrdinalIgnoreCase));

                if (projectInfo == null)
                    throw new InvalidOperationException("Project not found on server: " + projectName);

                DirectoryInfo dir = new DirectoryInfo(sessionDirectory);
                if (!dir.Exists) dir.Create();

                SessionCreationMode creationMode =
                    mode == "Multiuser" ? SessionCreationMode.Multiuser : SessionCreationMode.Exclusive;

                logger?.Invoke("Creating session: " + creationMode);

                LocalSessionInfo localInfo = server.CreateLocalSession(
                    projectInfo,
                    sessionName,
                    dir,
                    creationMode
                );

                // <<< TO JEST NAJWAŻNIEJSZE >>>
                localSessionProjectPath = localInfo.ProjectFileInfo.FullName;
                logger?.Invoke("Local session project file: " + localSessionProjectPath);

                logger?.Invoke("Opening local session...");
                LocalSession session = tia.LocalSessions.Open(localInfo.ProjectFileInfo);
                logger?.Invoke("Session opened.");

                return session;
            }
            catch (Exception ex)
            {
                logger?.Invoke("StartMultiuserSession EX: " + ex.Message);
                return null;
            }
        }



        public static string GetOpenedProjectPath(Action<string> logger = null)
        {
            if (tia == null)
                return null;

            try
            {
                var p = tia.Projects.FirstOrDefault();
                var full = p?.Path?.FullName;

                if (!string.IsNullOrWhiteSpace(full))
                    logger?.Invoke("Opened project in TIA: " + full);

                return full;
            }
            catch (Exception ex)
            {
                logger?.Invoke("GetOpenedProjectPath EX: " + ex.Message);
                return null;
            }
        }

        public static void OpenProjectInCurrentTia(string projectPath, Action<string> logger = null)
        {
            if (tia == null)
                throw new InvalidOperationException("TIA is not initialized.");

            if (string.IsNullOrWhiteSpace(projectPath))
                throw new InvalidOperationException("Project path is empty.");

            if (!File.Exists(projectPath))
                throw new FileNotFoundException("Project file not found: " + projectPath);

            logger?.Invoke("Opening project in attached TIA: " + projectPath);
            tia.Projects.Open(new FileInfo(projectPath));
        }


        public static void AttachToProcess(int processId, Action<string> logger)
        {
            try
            {
                var proc = TiaPortal.GetProcesses()
                    .FirstOrDefault(p => p.Id == processId);

                if (proc == null)
                {
                    logger($"Process {processId} not found.");
                    return;
                }

                tia = proc.Attach();
                logger("Attached to TIA process: " + processId);
            }
            catch (Exception ex)
            {
                logger("AttachToProcess EX: " + ex.Message);
            }
        }

        private static Project GetOrOpenProject(string projectPath, Action<string> logger)
        {
            if (tia == null)
                throw new InvalidOperationException("TIA not initialized. Call OpenTIA first.");

            // If any project already open, use it if it matches the desired path
            if (tia.Projects.Any())
            {
                var p = tia.Projects.First();
                try
                {
                    if (p.Path != null && p.Path.FullName != null &&
                        string.Equals(p.Path.FullName, projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        logger?.Invoke("Using already opened project: " + p.Path.Name);
                        return p;
                    }
                }
                catch { /* ignore */ }

                logger?.Invoke("A different project is already opened in TIA. Using the first opened project: " + p.Path.Name);
                return p;
            }

            var proj = tia.Projects.Open(new FileInfo(projectPath));
            logger?.Invoke("Opened project: " + proj.Path.Name);
            return proj;
        }

        private static IEnumerable<DeviceItem> Flatten(DeviceItem root)
        {
            yield return root;
            foreach (var c in root.DeviceItems)
                foreach (var x in Flatten(c))
                    yield return x;
        }

        public static List<CpuCandidate> GetCpuCandidates(string projectPath, string mode, Action<string> logger)
        {
            if (tia == null)
                OpenTIA(mode, logger);
            if (tia == null)
                throw new InvalidOperationException("TIA is not available.");

            var project = GetOrOpenProject(projectPath, logger);
            var list = new List<CpuCandidate>();

            foreach (var dev in project.Devices)
            {
                foreach (var di in dev.DeviceItems.SelectMany(Flatten))
                {
                    if (di.Classification != DeviceItemClassifications.CPU)
                        continue;

                    string order = SafeGetStringAttr(di, "OrderNumber");
                    string fw = SafeGetStringAttr(di, "FirmwareVersion");
                    string typeId = SafeGetStringAttr(di, "TypeIdentifierNormalized");
                    string name = SafeGetStringAttr(di, "Name");

                    list.Add(new CpuCandidate
                    {
                        DeviceName = dev.Name,
                        CpuName = string.IsNullOrWhiteSpace(name) ? di.Name : name,
                        OrderNumber = order,
                        FirmwareVersion = fw,
                        TypeIdentifierNormalized = typeId
                    });
                }
            }

            return list;
        }
        public static LocalSession OpenMuLocalSessionFile(string localSessionFilePath, bool openServerProjectView, Action<string> logger = null)
        {
            if (tia == null)
                throw new InvalidOperationException("TIA not initialized. Call OpenTIA first.");

            if (string.IsNullOrWhiteSpace(localSessionFilePath))
                throw new InvalidOperationException("Local session path is empty.");

            bool isFile = File.Exists(localSessionFilePath);
            bool isDir = Directory.Exists(localSessionFilePath);
            if (!isFile && !isDir)
                throw new FileNotFoundException("Local session not found (file or folder): " + localSessionFilePath);

            logger?.Invoke($"MU: Open local session: {localSessionFilePath} (serverView={openServerProjectView})");

            // Jeśli już jest otwarta sesja MU w tej instancji TIA – reuse
            try
            {
                if (tia.LocalSessions.Count > 0)
                {
                    logger?.Invoke("MU: LocalSession already open -> reuse existing.");
                    return tia.LocalSessions[0];
                }
            }
            catch { }

            // Uwaga: w praktyce .als19 bywa folderem – FileInfo też to przyjmie jako ścieżkę
            var fi = new FileInfo(localSessionFilePath);

            LocalSession session = openServerProjectView
                ? tia.LocalSessions.OpenServerProject(fi)
                : tia.LocalSessions.Open(fi);

            logger?.Invoke("MU: Open OK.");
            return session;
        }

        /// <summary>
        /// Save the active project in the currently opened local session and commit changes
        /// back to the multiuser server.  If a commit method is available on the
        /// LocalSession it will be used; otherwise the session will simply be closed
        /// after saving.  A short commit message can be provided to describe the
        /// changes.  This helper safely ignores errors and logs them instead of
        /// throwing exceptions to the caller.
        /// </summary>
        /// <param name="commitMessage">Commit message to use when committing changes.</param>
        /// <param name="logger">Delegate for logging progress or errors.</param>
        public static void CommitAndSaveLocalSession(string commitMessage, Action<string> logger = null)
        {
            try
            {
                if (tia == null)
                {
                    logger?.Invoke("Commit: TIA not initialized.");
                    return;
                }
                // If no local session is open, nothing to commit
                LocalSession session = null;
                try
                {
                    if (tia.LocalSessions.Count > 0)
                        session = tia.LocalSessions[0];
                }
                catch { /* ignore */ }
                if (session == null)
                {
                    logger?.Invoke("Commit: No local session open.");
                    return;
                }
                // For multiuser local sessions there is no separate Project.Save; saving is
                // handled implicitly when committing the session.  Attempting to call
                // proj.Save() on a MU session will throw an exception.  If a future
                // API exposes a save method for local sessions, you can call it here.
                // Attempt to commit and close the session.  Newer versions expose
                // CommitAndClose(string), older versions may only have Commit() and Close().
                bool committed = false;
                // Determine commit message to use
                string messageToUse = string.IsNullOrWhiteSpace(commitMessage) ? "Automated commit" : commitMessage;
                // Try to find and invoke CommitAndClose(string) via reflection
                try
                {
                    var commitAndClose = session.GetType().GetMethod(
                        "CommitAndClose", new Type[] { typeof(string) });
                    if (commitAndClose != null)
                    {
                        commitAndClose.Invoke(session, new object[] { messageToUse });
                        committed = true;
                        logger?.Invoke("Session committed and closed.");
                    }
                }
                catch (Exception ex)
                {
                    logger?.Invoke("Commit: CommitAndClose invoke error: " + ex.Message);
                    committed = false;
                }
                if (!committed)
                {
                    // Fallback: call Commit() then Close() if available
                    try
                    {
                        var commitMethod = session.GetType().GetMethod("Commit", Type.EmptyTypes);
                        commitMethod?.Invoke(session, null);
                        session.Close();
                        committed = true;
                        logger?.Invoke("Session committed and closed.");
                    }
                    catch
                    {
                        // Last resort: just close session
                        try
                        {
                            session.Close();
                            logger?.Invoke("Session closed (no commit method available).");
                        }
                        catch (Exception ex2)
                        {
                            logger?.Invoke("Commit: Closing session failed: " + ex2.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("CommitAndSaveLocalSession EX: " + ex.Message);
            }
        }

        public static void ApplyPlcSecurity(
            string projectPath,
            string mode,
            string catalogNumberNormalized,
            CpuCandidate targetExistingCpu,
            Action<string> logger)
        {
            if (tia == null)
                OpenTIA(mode, logger);
            if (tia == null)
                throw new InvalidOperationException("TIA is not available.");

            var project = GetOrOpenProject(projectPath, logger);

            DeviceItem cpu;
            if (!string.IsNullOrWhiteSpace(catalogNumberNormalized))
            {
                cpu = CreateCpuFromCatalogNumber(project, catalogNumberNormalized, logger);
            }
            else
            {
                if (targetExistingCpu == null)
                {
                    // fallback: if exactly one CPU exists, use it
                    var one = GetCpuCandidates(projectPath, mode, logger);
                    if (one.Count == 1)
                        targetExistingCpu = one[0];
                    else
                        throw new InvalidOperationException("No target CPU selected. Load CPUs and pick one from the list.");
                }

                cpu = FindCpuBySelection(project, targetExistingCpu);
            }

            // Local .apXX projects must remain without project-level protection.
            // UMAC users and roles are only configured through the Multiuser overload.
            logger?.Invoke("UMAC user preset skipped for local project; project protection remains disabled.");

            // Apply default PLC security settings
            ApplySecurityDefaults(cpu, logger);

            // Configure CPU access control via the dedicated provider.  In newer TIA versions
            // the PlcAccessControlConfiguration attribute is not writable on the DeviceItem
            // itself; it must be set through the PlcAccessControlConfigurationProvider.
            SetPlcAccessControl(cpu, logger);

            // After PLC security configuration, save the project to persist changes.
            try
            {
                project?.Save();
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Project save failed: {ex.Message}");
            }
        }

        private static DeviceItem FindCpuBySelection(Project project, CpuCandidate sel)
        {
            foreach (var dev in project.Devices)
            {
                if (!string.Equals(dev.Name, sel.DeviceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var di in dev.DeviceItems.SelectMany(Flatten))
                {
                    if (di.Classification != DeviceItemClassifications.CPU)
                        continue;

                    // Prefer exact name match first
                    string name = SafeGetStringAttr(di, "Name");
                    if (!string.IsNullOrWhiteSpace(name) && string.Equals(name, sel.CpuName, StringComparison.OrdinalIgnoreCase))
                        return di;
                    if (string.Equals(di.Name, sel.CpuName, StringComparison.OrdinalIgnoreCase))
                        return di;

                    // If user selected by TypeIdentifierNormalized (same model), still allow match
                    string typeId = SafeGetStringAttr(di, "TypeIdentifierNormalized");
                    if (!string.IsNullOrWhiteSpace(sel.TypeIdentifierNormalized) &&
                        string.Equals(typeId, sel.TypeIdentifierNormalized, StringComparison.OrdinalIgnoreCase))
                        return di;
                }
            }

            throw new InvalidOperationException("Selected CPU not found in project.");
        }

        private static void ApplySecurityDefaults(DeviceItem cpu, Action<string> logger)
        {
            // Apply a comprehensive set of security‐ and system‐related defaults for a new CPU.
            // These values were derived from a template PLC in TIA Portal (see user provided list).
            // Only attributes that materially influence the PLC configuration are set here; purely informational
            // properties (e.g. Author, AdditionalInformation, Comment) are intentionally skipped.

            if (cpu == null) return;

            // --- General diagnostic / alarm settings ---
            SetBoolAttr(cpu, "CentralAlarmManagement", true, logger);
            SetBoolAttr(cpu, "ClockMemoryByte", true, logger);
            SetUIntAttr(cpu, "ClockMemoryByteAddress", 0, logger);

            // Cycle settings
            SetUIntAttr(cpu, "CycleCommunicationLoad", 20, logger);
            SetBoolAttr(cpu, "CycleEnableMinimumCycleTime", true, logger);
            SetUIntAttr(cpu, "CycleMaximumCycleTime", 150, logger);
            SetUIntAttr(cpu, "CycleMinimumCycleTime", 1, logger);

            // Failsafe ranges (if supported by the device)
            SetUIntAttr(cpu, "Failsafe_CentralFSourceAddress", 1, logger);
            SetUIntAttr(cpu, "Failsafe_DefaultFmonitoringTimeCentral", 150, logger);
            SetUIntAttr(cpu, "Failsafe_LowerBoundForFDestinationAddresses", 1, logger);
            SetUIntAttr(cpu, "Failsafe_UpperBoundForFDestinationAddresses", 99, logger);

            // Networking and DNS behaviour
            SetBoolAttr(cpu, "HostAndDomainnameActive", false, logger);
            SetBoolAttr(cpu, "IPv4ForwardingActive", false, logger);
            SetBoolAttr(cpu, "IsochronousMode", false, logger);
            SetBoolAttr(cpu, "NetworkFaultsAsMaintenance", false, logger);

            // System memory settings
            SetBoolAttr(cpu, "SystemMemoryByte", true, logger);
            SetUIntAttr(cpu, "SystemMemoryByteAddress", 1, logger);

            // Daylight saving and timezone configuration
            SetBoolAttr(cpu, "TimeOfDayActivateDaylightSavingTime", true, logger);
            SetUIntAttr(cpu, "TimeOfDayDaylightSavingTimeOffset", 60, logger);
            SetUIntAttr(cpu, "TimeOfDayDaylightSavingTimeStartHour", 2, logger);
            SetUIntAttr(cpu, "TimeOfDayDaylightSavingTimeStartMonth", 3, logger);
            SetUIntAttr(cpu, "TimeOfDayDaylightSavingTimeStartWeek", 5, logger);
            SetUIntAttr(cpu, "TimeOfDayDaylightSavingTimeStartWeekday", 1, logger);
            SetUIntAttr(cpu, "TimeOfDayLocalTimeZone", 26, logger);
            SetUIntAttr(cpu, "TimeOfDayStandardTimeStartHour", 3, logger);
            SetUIntAttr(cpu, "TimeOfDayStandardTimeStartMonth", 10, logger);
            SetUIntAttr(cpu, "TimeOfDayStandardTimeStartWeek", 5, logger);
            SetUIntAttr(cpu, "TimeOfDayStandardTimeStartWeekday", 1, logger);

            // Time synchronization servers
            SetStringAttr(cpu, "TimeSynchronizationServer1", "10.201.23.2", logger);
            SetStringAttr(cpu, "TimeSynchronizationServer2", string.Empty, logger);
            SetStringAttr(cpu, "TimeSynchronizationServer3", string.Empty, logger);
            SetStringAttr(cpu, "TimeSynchronizationServer4", string.Empty, logger);
            SetUIntAttr(cpu, "TimeSynchronizationUpdateInterval", 10, logger);

            // Diagnostic alarm ID configuration
            SetBoolAttr(cpu, "UseFixedSystemDiagnosticsAlarmIds", true, logger);

            // Web server and SNMP settings
            SetBoolAttr(cpu, "WebserverActivate", false, logger);
            SetBoolAttr(cpu, "SNMPActive", false, logger);
            SetBoolAttr(cpu, "SNMPReadOnlyActive", false, logger);
            SetStringAttr(cpu, "SNMPReadOnlyCommunityName", "public", logger);
            SetStringAttr(cpu, "SNMPReadWriteCommunityName", "private", logger);

            // PLC access control: enable access control with keys. The enumeration value
            // "EnabledWithAccessControl" corresponds to requiring a password for online access.
            // We attempt to set it via string to avoid hard dependencies on the enum type.
            SetStringAttr(cpu, "PlcAccessControlConfiguration", "EnabledWithAccessControl", logger);

            // DNS configuration – deactivate name resolution
            SetStringAttr(cpu, "PnDnsConfigNameResolve", "Deactivated", logger);

            // Security event summarisation configuration
            SetUIntAttr(cpu, "ProtectionIntervalForSummarizeOfSecurityEvents", 20, logger);
            SetBoolAttr(cpu, "ProtectionSummarizeSecurityEventsOnHighLoad", true, logger);
            SetUIntAttr(cpu, "ProtectionUnitForSummarizeOfSecurityEvents", 2, logger);

            // Finally enable PUT/GET communication. This must be set after access control to avoid
            // unexpected side effects (as noted by the user). Put/Get allows external clients
            // to read/write global DBs via the PG/PC interface.
            SetBoolAttr(cpu, "ProtectionEnablePutGetCommunication", true, logger);
        }

        // Helper to set UInt64 attributes safely.
        private static void SetUIntAttr(DeviceItem item, string attr, ulong value, Action<string> logger)
        {
            // Unified setter via smart attribute conversion; numeric values are converted accordingly
            SetAttrSmart(item, attr, value, logger);
        }

        private static void SetBoolAttr(DeviceItem item, string attr, bool value, Action<string> logger)
        {
            // Unified setter via smart attribute conversion
            SetAttrSmart(item, attr, value, logger);
        }

        private static void SetStringAttr(DeviceItem item, string attr, string value, Action<string> logger)
        {
            // Unified setter via smart attribute conversion
            SetAttrSmart(item, attr, value, logger);
        }

        private static string SafeGetStringAttr(DeviceItem item, string attr)
        {
            try
            {
                var v = item.GetAttribute(attr);
                return v?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Sets an attribute on a DeviceItem using type‑aware conversion.  This helper inspects the target
        /// attribute's ValueType (if available) and converts the supplied value accordingly (e.g. string to enum,
        /// numeric narrowing) before writing.  Unsupported attributes are skipped with an informational log.
        /// </summary>
        private static void SetAttrSmart(DeviceItem item, string attrName, object desired, Action<string> logger)
        {
            try
            {
                // Attempt to locate the AttributeInfo for the given name
                var infos = item.GetAttributeInfos();
                var info = infos.FirstOrDefault(ai => string.Equals(ai.Name, attrName, StringComparison.Ordinal));
                if (info == null)
                {
                    logger?.Invoke($"SKIP {item.Name}.{attrName} (attribute not supported)");
                    return;
                }

                // Use reflection to obtain the target data type; not all API versions expose ValueType property
                Type targetType = null;
                try
                {
                    var vtProp = info.GetType().GetProperty("ValueType");
                    if (vtProp != null)
                        targetType = vtProp.GetValue(info) as Type;
                    if (targetType == null)
                    {
                        var dtProp = info.GetType().GetProperty("DataType");
                        if (dtProp != null)
                            targetType = dtProp.GetValue(info) as Type;
                    }
                }
                catch { }

                object valueToSet = desired;

                if (targetType != null && desired != null)
                {
                    var strDesired = desired as string;
                    // Handle enums by parsing from string (case‑insensitive)
                    if (targetType.IsEnum && strDesired != null)
                    {
                        try
                        {
                            valueToSet = Enum.Parse(targetType, strDesired, ignoreCase: true);
                        }
                        catch
                        {
                            // ignore parse failures
                        }
                    }
                    // Numeric conversions (common in TIA attributes)
                    else if (targetType == typeof(uint))
                    {
                        if (desired is ulong ul) valueToSet = (uint)ul;
                        else if (strDesired != null && uint.TryParse(strDesired, out var u32)) valueToSet = u32;
                    }
                    else if (targetType == typeof(ulong))
                    {
                        if (desired is uint u32) valueToSet = (ulong)u32;
                        else if (strDesired != null && ulong.TryParse(strDesired, out var ul2)) valueToSet = ul2;
                    }
                    else if (targetType == typeof(bool))
                    {
                        if (strDesired != null && bool.TryParse(strDesired, out var b)) valueToSet = b;
                    }
                    // If desired is string and target is numeric primitive, attempt conversion
                    else if (targetType.IsPrimitive && strDesired != null)
                    {
                        try
                        {
                            valueToSet = Convert.ChangeType(strDesired, targetType, CultureInfo.InvariantCulture);
                        }
                        catch { }
                    }
                }

                // Read current value to avoid redundant writes
                object before = null;
                try { before = item.GetAttribute(attrName); } catch { }
                // If unchanged, skip silently
                if (before != null && before.Equals(valueToSet))
                {
                    logger?.Invoke($"SKIP {item.Name}.{attrName} (unchanged)");
                    return;
                }
                item.SetAttribute(attrName, valueToSet);
                object after = null;
                try { after = item.GetAttribute(attrName); } catch { }
                logger?.Invoke($"Set {item.Name}.{attrName} = {after}");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"WARN: Cannot set {item.Name}.{attrName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Dumps available attributes and their ValueType for a DeviceItem.  Use the optional filter to limit
        /// output to attribute names containing the substring (case‑insensitive).  This is useful for
        /// discovering correct attribute names and types on specific CPU models.
        /// </summary>
        private static void DumpAttrs(DeviceItem item, Action<string> logger, string contains = null)
        {
            try
            {
                foreach (var ai in item.GetAttributeInfos())
                {
                    var name = ai.Name;
                    if (contains == null || name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Obtain the attribute type via reflection; not all API versions expose ValueType
                        string typeName = "(unknown)";
                        try
                        {
                            var vtProp = ai.GetType().GetProperty("ValueType");
                            var dtProp = ai.GetType().GetProperty("DataType");
                            Type vt = null;
                            if (vtProp != null) vt = vtProp.GetValue(ai) as Type;
                            if (vt == null && dtProp != null) vt = dtProp.GetValue(ai) as Type;
                            if (vt != null) typeName = vt.FullName;
                        }
                        catch { }
                        logger?.Invoke($"ATTR: {name} | Type={typeName}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"DumpAttrs failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Configures the PLC access control through the dedicated provider.  This is
        /// necessary in newer TIA versions where the PlcAccessControlConfiguration
        /// attribute is not directly writable on the DeviceItem.  The configuration
        /// is set to EnabledWithAccessControl (value 1).  If the provider is not
        /// available, this method logs a warning and returns.
        /// </summary>
        private static void SetPlcAccessControl(DeviceItem cpu, Action<string> logger)
        {
            if (cpu == null) return;
            try
            {
                // Attempt to obtain the PlcAccessControlConfigurationProvider using reflection
                var cpuType = cpu.GetType();
                var getServiceMethod = cpuType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance);
                if (getServiceMethod != null)
                {
                    var hwAssembly = typeof(DeviceItem).Assembly;
                    var providerType = hwAssembly.GetType("Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider", false);
                    if (providerType != null)
                    {
                        var generic = getServiceMethod.MakeGenericMethod(providerType);
                        var provider = generic.Invoke(cpu, null);
                        if (provider != null)
                        {
                            // The provider may expose SetAttribute(string,int) or a property
                            bool success = false;
                            // Try SetAttribute first
                            var setAttr = providerType.GetMethod("SetAttribute", new[] { typeof(string), typeof(int) });
                            if (setAttr != null)
                            {
                                // 1 corresponds to EnabledWithAccessControl
                                setAttr.Invoke(provider, new object[] { "PlcAccessControlConfiguration", 1 });
                                success = true;
                            }
                            else
                            {
                                // Try property
                                var prop = providerType.GetProperty("PlcAccessControlConfiguration");
                                if (prop != null && prop.CanWrite)
                                {
                                    var enumType = prop.PropertyType;
                                    object val;
                                    if (enumType.IsEnum)
                                    {
                                        // Try to parse the enum by name
                                        try
                                        {
                                            val = Enum.Parse(enumType, "EnabledWithAccessControl", true);
                                        }
                                        catch
                                        {
                                            // Fallback to numeric value 1 if parsing fails
                                            val = 1;
                                        }
                                    }
                                    else
                                    {
                                        val = 1;
                                    }
                                    prop.SetValue(provider, val);
                                    success = true;
                                }
                            }

                            // Commit or save if available
                            var saveMethod = providerType.GetMethod("Save", Type.EmptyTypes) ?? providerType.GetMethod("Commit", Type.EmptyTypes);
                            saveMethod?.Invoke(provider, null);

                            if (success)
                                logger?.Invoke("PLC access control set via provider.");
                        }
                        else
                        {
                            logger?.Invoke("PlcAccessControlConfigurationProvider not available on CPU.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Failed to set PLC access control: {ex.Message}");
            }
        }

        private static DeviceItem CreateCpuFromCatalogNumber(Project project, string catalogNumberNormalized, Action<string> logger)
        {
            // Accept either full type id ("OrderNumber:.../Vx.y") or just order number ("6ES7...").
            var input = (catalogNumberNormalized ?? string.Empty).Trim().Replace(" ", "");
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Catalog number is empty.");

            string typeId;
            if (input.StartsWith("OrderNumber:", StringComparison.OrdinalIgnoreCase))
            {
                typeId = input; // already a TypeIdentifierNormalized
            }
            else
            {
                // Lookup in HardwareCatalog by OrderNumber, pick the first match.
                typeId = ResolveTypeIdFromHardwareCatalog(input, logger);
            }

            // Create device with temporary names; user can rename later.
            string tmpDeviceName = "station_" + DateTime.Now.ToString("HHmmss");
            string tmpItemName = "PLC_" + DateTime.Now.ToString("HHmmss");

            Device device = project.UngroupedDevicesGroup.Devices.CreateWithItem(typeId, tmpItemName, tmpDeviceName);
            logger?.Invoke($"Created device from catalog: {typeId} -> {device.Name}");

            // Find CPU item inside created device
            var cpu = device.DeviceItems.SelectMany(Flatten).FirstOrDefault(di => di.Classification == DeviceItemClassifications.CPU);
            if (cpu == null)
                throw new InvalidOperationException("Created device but CPU item not found.");
            return cpu;
        }

        // -------------------------------------------------------------------------
        // Helper methods for import and compile
        //
        // These helpers attempt to resolve and invoke the appropriate overload of the
        // Blocks.Import method using reflection.  Different versions of the TIA
        // Openness API expose different Import signatures (e.g. accepting FileInfo,
        // string, collections or requiring ImportOptions).  TryImportBlocks will
        // iterate through all available Import methods, construct suitable
        // argument lists and attempt to invoke them.  Any exceptions are logged and
        // the next candidate overload is tried.  If no overload succeeds, the
        // method returns false.
        private static bool TryImportBlocks(object blocks, FileInfo fileInfo, Action<string> logger)
        {
            if (blocks == null) return false;

            var t = blocks.GetType();
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                           .Where(m => m.Name == "Import")
                           .ToList();

            if (methods.Count == 0)
            {
                logger?.Invoke("No Import methods found on Blocks type: " + t.FullName);
                return false;
            }

            foreach (var m in methods.OrderByDescending(mm => mm.GetParameters().Length))
            {
                var ps = m.GetParameters();
                object[] args = null;

                if (ps.Length == 1)
                {
                    args = BuildImportArgs1(ps[0].ParameterType, fileInfo);
                }
                else if (ps.Length == 2)
                {
                    args = BuildImportArgs2(ps[0].ParameterType, ps[1].ParameterType, fileInfo);
                }

                if (args == null) continue;

                try
                {
                    logger?.Invoke($"Trying Import overload: Import({string.Join(", ", ps.Select(p => p.ParameterType.Name))})");
                    m.Invoke(blocks, args);
                    return true;
                }
                catch (TargetInvocationException tie)
                {
                    logger?.Invoke("Import overload failed: " + (tie.InnerException?.Message ?? tie.Message));
                }
                catch (Exception ex)
                {
                    logger?.Invoke("Import overload failed: " + ex.Message);
                }
            }

            return false;
        }

        // Construct arguments for a single-parameter Import method based on parameter type.
        private static object[] BuildImportArgs1(Type p0, FileInfo fileInfo)
        {
            if (p0 == typeof(FileInfo)) return new object[] { fileInfo };
            if (p0 == typeof(string)) return new object[] { fileInfo.FullName };

            // FileInfo[]
            if (p0.IsArray && p0.GetElementType() == typeof(FileInfo))
                return new object[] { new[] { fileInfo } };

            // IEnumerable<FileInfo>
            if (p0.IsGenericType &&
                p0.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                p0.GetGenericArguments()[0] == typeof(FileInfo))
                return new object[] { new[] { fileInfo } };

            return null;
        }

        // Construct arguments for a two-parameter Import method.  The second parameter
        // may be an enum (ImportOptions), bool, int or string.  We attempt to
        // choose sensible defaults.
        private static object[] BuildImportArgs2(Type p0, Type p1, FileInfo fileInfo)
        {
            var a1 = BuildImportArgs1(p0, fileInfo);
            if (a1 == null) return null;

            object opt = null;

            // Enum (e.g. ImportOptions)
            if (p1.IsEnum)
            {
                opt = TryParseEnum(p1, "WithDefaults")
                   ?? TryParseEnum(p1, "Override")
                   ?? Enum.GetValues(p1).GetValue(0);
            }
            else if (p1 == typeof(bool))
            {
                opt = true;
            }
            else if (p1 == typeof(int))
            {
                opt = 0;
            }
            else if (p1 == typeof(string))
            {
                opt = "WithDefaults";
            }

            if (opt == null) return null;
            return new object[] { a1[0], opt };
        }

        // Parse an enum value by name, returning null if parsing fails.
        private static object TryParseEnum(Type enumType, string name)
        {
            try { return Enum.Parse(enumType, name, ignoreCase: true); }
            catch { return null; }
        }

        // Attempt to compile the PLC.  Prefer using ICompilable service if available,
        // otherwise fall back to invoking Compile() on the PlcSoftware itself.
        private static void TryCompilePlc(Siemens.Engineering.SW.PlcSoftware plc, Action<string> logger)
        {
            if (plc == null) return;

            // First attempt to obtain an ICompilable service via GetService<T>()
            try
            {
                var asm = plc.GetType().Assembly;
                var iCompilableType = asm.GetType("Siemens.Engineering.Compiler.ICompilable", throwOnError: false);

                if (iCompilableType != null)
                {
                    var getService = plc.GetType()
                        .GetMethods()
                        .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

                    if (getService != null)
                    {
                        var generic = getService.MakeGenericMethod(iCompilableType);
                        var svc = generic.Invoke(plc, null);

                        if (svc != null)
                        {
                            var compile = iCompilableType.GetMethod("Compile", Type.EmptyTypes);
                            if (compile != null)
                            {
                                logger?.Invoke("Compiling via ICompilable...");
                                compile.Invoke(svc, null);
                                logger?.Invoke("Compilation finished (ICompilable).");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("Compile via ICompilable failed: " + ex.Message);
            }

            // Fallback: call Compile() directly if available
            try
            {
                var compileMethod = plc.GetType().GetMethod("Compile", Type.EmptyTypes);
                if (compileMethod != null)
                {
                    logger?.Invoke("Compiling via PlcSoftware.Compile()...");
                    compileMethod.Invoke(plc, null);
                    logger?.Invoke("Compilation finished (PlcSoftware.Compile).");
                }
                else
                {
                    logger?.Invoke("No compile method available on this PlcSoftware version.");
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("Compile fallback failed: " + ex.Message);
            }
        }

        private static string ResolveTypeIdFromHardwareCatalog(string orderNumberNormalized, Action<string> logger)
        {
            if (tia == null)
                throw new InvalidOperationException("TIA not initialized.");

            object hwCat;
            try
            {
                var prop = tia.GetType().GetProperty("HardwareCatalog");
                hwCat = prop?.GetValue(tia);
            }
            catch
            {
                hwCat = null;
            }

            if (hwCat == null)
                throw new InvalidOperationException("HardwareCatalog not available on this TIA/Openness setup.");

            // find root folder property (Root/RootFolder)
            object root = null;
            var rootProp = hwCat.GetType().GetProperties()
                .FirstOrDefault(p => p.Name.IndexOf("root", StringComparison.OrdinalIgnoreCase) >= 0);
            if (rootProp != null)
                root = rootProp.GetValue(hwCat);

            if (root == null)
                throw new InvalidOperationException("HardwareCatalog root folder not found.");

            string wantedPrefix = "OrderNumber:" + orderNumberNormalized;
            foreach (var entry in EnumerateCatalogEntries(root))
            {
                string typeNorm = GetPropString(entry, "TypeIdentifierNormalized");
                if (string.IsNullOrWhiteSpace(typeNorm))
                    continue;

                if (typeNorm.StartsWith(wantedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    logger?.Invoke("Catalog match: " + typeNorm);
                    return typeNorm;
                }
            }

            throw new InvalidOperationException($"No hardware catalog entry found for order number: {orderNumberNormalized}");
        }

        private static IEnumerable<object> EnumerateCatalogEntries(object folder)
        {
            // folder.Entries + folder.Folders, but use reflection for version resilience
            if (folder == null) yield break;

            var t = folder.GetType();
            var entriesProp = t.GetProperty("Entries");
            var foldersProp = t.GetProperty("Folders");

            var entries = entriesProp?.GetValue(folder) as System.Collections.IEnumerable;
            if (entries != null)
            {
                foreach (var e in entries)
                    yield return e;
            }

            var folders = foldersProp?.GetValue(folder) as System.Collections.IEnumerable;
            if (folders != null)
            {
                foreach (var f in folders)
                {
                    foreach (var e in EnumerateCatalogEntries(f))
                        yield return e;
                }
            }
        }

        private static string GetPropString(object obj, string propName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propName);
                var v = p?.GetValue(obj);
                return v?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }











        /// <summary>
        /// Convenience wrapper used by the wizard UI.
        /// If catalogNumberNormalized is empty, the method will configure an existing CPU in the project
        /// (if exactly one CPU exists it will be used automatically).
        /// If catalogNumberNormalized is provided, a new CPU/device is created from hardware catalog and configured.
        /// </summary>
        public static void ApplyPlcSecurityDefaults(
            string projectPath,
            string catalogNumberNormalized,
            string mode,
            Action<string> logger)
        {
            // Reuse the existing, working implementation to avoid behavior drift.
            ApplyPlcSecurity(projectPath, mode, catalogNumberNormalized, null, logger);
        }

        public static void ApplyPlcSecurityDefaults(
            string projectPath,
            string catalogNumberNormalized,
            CpuCandidate targetExistingCpu,
            string mode,
            Action<string> logger)
        {
            ApplyPlcSecurity(projectPath, mode, catalogNumberNormalized, targetExistingCpu, logger);
        }

        public static void RunWithGui(
              string projectPath,
              string csvPath,
              string deviceType,
              Action<string> logger,
              string masterAddressMode = "Sequential")
        {
            if (logger == null) logger = (s) => { };
            logger("Run begin (CSV ONLY mode)");

            try
            {
                if (tia == null)
                    OpenTIA("attach", logger);

                if (tia == null)
                {
                    logger("TIA == null");
                    return;
                }

                // ✅ JEŚLI JUŻ JEST OTWARTY PROJEKT → NIE OTWIERAMY DRUGI RAZ
                Project project = null;
                if (tia.Projects.Any())
                {
                    project = tia.Projects.First();
                    logger("Using already opened project: " + project.Path.Name);
                }
                else
                {
                    project = tia.Projects.Open(new FileInfo(projectPath));
                    logger("Opened project: " + project.Path.Name);
                }

                // ✅ CZYTANIE CSV — WERSJA ODPORNA NA , ; \t
                if (deviceType == "Hik3050")
                {
                    ImportHik3050(project, csvPath, logger);
                    return;
                }

                var rows = File.ReadAllLines(csvPath);
                if (rows.Length < 2)
                {
                    logger("CSV is empty");
                    return;
                }

                char[] sep = new char[] { ';', ',', '\t' };
                string[] headers = rows[0].Split(sep, StringSplitOptions.None)
                                          .Select(h => h.Trim())
                                          .ToArray();

                var pepperlSettings = new PepperlAddressingSettings
                {
                    Mode = ParsePepperlAddressMode(masterAddressMode)
                };
                var pepperlState = new PepperlAddressingState();

                for (int i = 1; i < rows.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(rows[i])) continue;

                    string[] values = rows[i].Split(sep, StringSplitOptions.None)
                                             .Select(v => v.Trim())
                                             .ToArray();

                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int j = 0; j < headers.Length && j < values.Length; j++)
                        dict[headers[j]] = values[j];

                    // ✅ LOG KONTROLNY — TO MUSI POKAZAĆ GSD, NIE IP
                    if (dict.ContainsKey("Type"))
                        logger("CSV TYPE = " + dict["Type"]);
                    else
                        logger("CSV TYPE = <BRAK KOLUMNY Type>");


                    if (deviceType == "Lenze")
                    {
                        var d = MapToLenze(dict);

                        // ✅ STOP dla Lenze: Name lub DeviceName = 0 lub puste
                        if (string.IsNullOrWhiteSpace(d.Name) ||
                            string.IsNullOrWhiteSpace(d.DeviceName) ||
                            d.Name.Trim() == "0" ||
                            d.DeviceName.Trim() == "0")
                        {
                            logger("LENZE Name/DeviceName = 0 lub puste → kończę dodawanie urządzeń");
                            break; // ← przerywa dalsze czytanie CSV
                        }

                        var dev = AddLenzeDevice(project, d, logger);
                        logger(dev != null ? "Lenze added: " + d.Name : "Lenze failed: " + d.Name);
                    }

                    else
                    {
                        if (deviceType == "Camera")
                        {
                            var c = MapToKeyenceCamera(dict);
                            if (string.IsNullOrWhiteSpace(c.Name) ||
                                c.Name.Trim() == "0")
                            {
                                logger("CAMERA Name = 0 lub puste → kończę dodawanie kamer");
                                break;
                            }

                            var cam = AddKeyenceCameraDevice(project, c, logger);
                            logger(cam != null ? "Camera added: " + c.Name : "Camera failed: " + c.Name);
                            continue;
                        }

                        var p = MapToPepperl(dict);

                        // ✅ JEŚLI MASTER NAME == "0" → KOŃCZYMY DODAWANIE MASTERÓW
                        if (p.Name != null && p.Name.Trim() == "0")
                        {
                            logger("MASTER Name == 0 → kończę dodawanie masterów (CSV przerwane)");
                            break; // ← TO JEST KLUCZOWE
                        }

                        // ✅ DOPIERO TERAZ WOLNO WYWOŁAĆ TIA
                        var dev = AddPepperlDevice(project, p, pepperlSettings, pepperlState, logger);
                        logger(dev != null ? "Pepperl added: " + p.Name : "Pepperl failed: " + p.Name);

                    }
                }

                logger("Run done");
            }
            catch (Exception ex)
            {
                logger("Run EX: " + ex.Message);
            }
        }

        /// <summary>
        /// Import devices from a CSV file directly into an already opened Project instance.
        /// This method should be used when working inside a LocalSession (e.g. Multiuser)
        /// where there is no .apXX project file on disk.  It mirrors the behaviour
        /// of RunWithGui, but operates on an in-memory project instead of opening
        /// the project from disk.  The CSV must have the same column structure as
        /// expected by the original C# implementation.
        /// </summary>
        /// <param name="project">The Project instance to modify.</param>
        /// <param name="csvPath">Path to the CSV file.</param>
        /// <param name="deviceType">"Lenze" or "Master".</param>
        /// <param name="logger">Logging delegate.</param>
        public static void ImportDevicesFromCsv(
            Project project,
            string csvPath,
            string deviceType,
            Action<string> logger,
            string masterAddressMode = "Sequential")
        {
            if (logger == null) logger = s => { };
            if (project == null)
            {
                logger("ImportDevicesFromCsv: Project is null");
                return;
            }
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                logger("ImportDevicesFromCsv: CSV file not found: " + csvPath);
                return;
            }
            try
            {
                if (deviceType == "Hik3050")
                {
                    ImportHik3050(project, csvPath, logger);
                    return;
                }

                var rows = File.ReadAllLines(csvPath);
                if (rows.Length < 2)
                {
                    logger("CSV is empty");
                    return;
                }
                char[] sep = new char[] { ';', ',', '\t' };
                string[] headers = rows[0].Split(sep, StringSplitOptions.None)
                                          .Select(h => h.Trim())
                                          .ToArray();
                var pepperlSettings = new PepperlAddressingSettings
                {
                    Mode = ParsePepperlAddressMode(masterAddressMode)
                };
                var pepperlState = new PepperlAddressingState();
                for (int i = 1; i < rows.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(rows[i])) continue;
                    string[] values = rows[i].Split(sep, StringSplitOptions.None)
                                             .Select(v => v.Trim())
                                             .ToArray();
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int j = 0; j < headers.Length && j < values.Length; j++)
                        dict[headers[j]] = values[j];

                    if (deviceType == "Lenze")
                    {
                        var d = MapToLenze(dict);
                        if (string.IsNullOrWhiteSpace(d.Name) ||
                            string.IsNullOrWhiteSpace(d.DeviceName) ||
                            d.Name.Trim() == "0" ||
                            d.DeviceName.Trim() == "0")
                        {
                            logger("LENZE Name/DeviceName = 0 lub puste → kończę dodawanie urządzeń");
                            break;
                        }
                        var dev = AddLenzeDevice(project, d, logger);
                        logger(dev != null ? "Lenze added: " + d.Name : "Lenze failed: " + d.Name);
                    }
                    else
                    {
                        if (deviceType == "Camera")
                        {
                            var c = MapToKeyenceCamera(dict);
                            if (string.IsNullOrWhiteSpace(c.Name) ||
                                c.Name.Trim() == "0")
                            {
                                logger("CAMERA Name = 0 lub puste → kończę dodawanie kamer");
                                break;
                            }
                            var cam = AddKeyenceCameraDevice(project, c, logger);
                            logger(cam != null ? "Camera added: " + c.Name : "Camera failed: " + c.Name);
                            continue;
                        }

                        var p = MapToPepperl(dict);
                        if (p.Name != null && p.Name.Trim() == "0")
                        {
                            logger("MASTER Name == 0 → kończę dodawanie masterów (CSV przerwane)");
                            break;
                        }
                        var dev = AddPepperlDevice(project, p, pepperlSettings, pepperlState, logger);
                        logger(dev != null ? "Pepperl added: " + p.Name : "Pepperl failed: " + p.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger("ImportDevicesFromCsv EX: " + ex.Message);
            }
        }

        /// <summary>
        /// Import devices from a CSV file directly into a MultiuserProject instance.
        /// This overload is required because a Siemens.Engineering.Multiuser.MultiuserProject
        /// cannot be implicitly converted to a Siemens.Engineering.Project.  The logic
        /// mirrors the Project-based ImportDevicesFromCsv method but operates on a
        /// MultiuserProject.  No simplification of device creation logic is performed.
        /// </summary>
        /// <param name="project">The MultiuserProject instance to modify.</param>
        /// <param name="csvPath">Path to the CSV file.</param>
        /// <param name="deviceType">"Lenze" or "Master".</param>
        /// <param name="logger">Logging delegate.</param>
        public static void ImportDevicesFromCsv(
            Siemens.Engineering.Multiuser.MultiuserProject project,
            string csvPath,
            string deviceType,
            Action<string> logger,
            string masterAddressMode = "Sequential")
        {
            if (logger == null) logger = s => { };
            if (project == null)
            {
                logger("ImportDevicesFromCsv: MultiuserProject is null");
                return;
            }
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                logger("ImportDevicesFromCsv: CSV file not found: " + csvPath);
                return;
            }
            try
            {
                if (deviceType == "Hik3050")
                {
                    ImportHik3050(project, csvPath, logger);
                    return;
                }

                var rows = File.ReadAllLines(csvPath);
                if (rows.Length < 2)
                {
                    logger("CSV is empty");
                    return;
                }
                char[] sep = new char[] { ';', ',', '\t' };
                string[] headers = rows[0].Split(sep, StringSplitOptions.None)
                                          .Select(h => h.Trim())
                                          .ToArray();
                var pepperlSettings = new PepperlAddressingSettings
                {
                    Mode = ParsePepperlAddressMode(masterAddressMode)
                };
                var pepperlState = new PepperlAddressingState();
                for (int i = 1; i < rows.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(rows[i])) continue;
                    string[] values = rows[i].Split(sep, StringSplitOptions.None)
                                             .Select(v => v.Trim())
                                             .ToArray();
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int j = 0; j < headers.Length && j < values.Length; j++)
                        dict[headers[j]] = values[j];
                    if (deviceType == "Lenze")
                    {
                        var d = MapToLenze(dict);
                        if (string.IsNullOrWhiteSpace(d.Name) ||
                            string.IsNullOrWhiteSpace(d.DeviceName) ||
                            d.Name.Trim() == "0" ||
                            d.DeviceName.Trim() == "0")
                        {
                            logger("LENZE Name/DeviceName = 0 lub puste → kończę dodawanie urządzeń");
                            break;
                        }
                        var dev = AddLenzeDevice(project, d, logger);
                        logger(dev != null ? "Lenze added: " + d.Name : "Lenze failed: " + d.Name);
                    }
                    else
                    {
                        if (deviceType == "Camera")
                        {
                            var c = MapToKeyenceCamera(dict);
                            if (string.IsNullOrWhiteSpace(c.Name) ||
                                c.Name.Trim() == "0")
                            {
                                logger("CAMERA Name = 0 lub puste → kończę dodawanie kamer");
                                break;
                            }
                            var cam = AddKeyenceCameraDevice(project, c, logger);
                            logger(cam != null ? "Camera added: " + c.Name : "Camera failed: " + c.Name);
                            continue;
                        }

                        var p = MapToPepperl(dict);
                        if (p.Name != null && p.Name.Trim() == "0")
                        {
                            logger("MASTER Name == 0 → kończę dodawanie masterów (CSV przerwane)");
                            break;
                        }
                        var dev = AddPepperlDevice(project, p, pepperlSettings, pepperlState, logger);
                        logger(dev != null ? "Pepperl added: " + p.Name : "Pepperl failed: " + p.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger("ImportDevicesFromCsv EX: " + ex.Message);
            }
        }

        /// <summary>
        /// Create and configure a Lenze device in a MultiuserProject.  This overload
        /// mirrors the logic of the Project-based AddLenzeDevice method and is
        /// required because MultiuserProject cannot be implicitly converted to
        /// Project.  All address mapping and networking logic is retained.
        /// </summary>
        private static Device AddLenzeDevice(Siemens.Engineering.Multiuser.MultiuserProject project, LenzeDevice hwDevice, Action<string> logger)
        {
            try
            {
                // Create device with name and device name
                Device device = project.UngroupedDevicesGroup.Devices.CreateWithItem(hwDevice.Type, hwDevice.Name, hwDevice.DeviceName);
                if (device == null) { logger("Create device returned null"); return null; }

                // addresses - best effort
                try
                {
                    if (device.DeviceItems.Count > 7)
                    {
                        device.DeviceItems[2].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 4);
                        device.DeviceItems[3].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 0);
                        device.DeviceItems[4].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 0);
                        device.DeviceItems[5].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 2);
                        device.DeviceItems[6].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 2);
                        device.DeviceItems[7].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 4);
                    }
                }
                catch { logger("Lenze address mapping failed"); }

                var netIf = GetNetworkInterface(device);
                if (netIf == null) { logger("No network interface on device"); return device; }

                // Access subnets directly from the multiuser project
                var subnet = project.Subnets.FirstOrDefault(s => s.Name.Equals(hwDevice.SubnetName, StringComparison.OrdinalIgnoreCase));
                if (subnet == null) { logger("Subnet not found: " + hwDevice.SubnetName); return device; }

                var node = netIf.Nodes.FirstOrDefault();
                if (node == null) { logger("No node"); return device; }
                node.ConnectToSubnet(subnet);
                node.SetAttribute("Address", hwDevice.IpAddress);

                var ioSystem = subnet.IoSystems.FirstOrDefault(a =>
                    a.HwIdentifiers?[0]?.HwIdentifierControllers?[0]?.GetAttribute("Name")?.Equals(hwDevice.PlcName) == true);
                if (ioSystem == null) ioSystem = subnet.IoSystems.FirstOrDefault();
                if (ioSystem == null) { logger("IoSystem not found"); return device; }

                var ioConnector = netIf.IoConnectors.FirstOrDefault();
                if (ioConnector == null) { logger("No IoConnector"); return device; }
                ioConnector.ConnectToIoSystem(ioSystem);
                ioConnector.SetAttribute("PnDeviceNumber", hwDevice.ProfinetNumber);

                return device;
            }
            catch (Exception ex)
            {
                logger("AddLenze ex: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Create and configure a Pepperl device in a MultiuserProject.  This overload
        /// mirrors the logic of the Project-based AddPepperlDevice method and is
        /// required because MultiuserProject cannot be implicitly converted to
        /// Project.  All name collision handling, module plugging and network
        /// configuration logic is retained.
        /// </summary>
        private static Device AddPepperlDevice(Siemens.Engineering.Multiuser.MultiuserProject project, PepperlDevice pepperlDevice, PepperlAddressingSettings settings, PepperlAddressingState state, Action<string> logger)
        {
            if (pepperlDevice.Name == "0")
            {
                logger("AddPepperl ABORT — Name == 0");
                return null;
            }
            try
            {
                // name collision protection
                string finalName = pepperlDevice.Name;
                int suffix = 1;
                while (project.UngroupedDevicesGroup.Devices.Any(d => d.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase)))
                {
                    finalName = pepperlDevice.Name + "_" + suffix;
                    suffix++;
                }
                string finalDeviceName = finalName + "_DEV";
                Device device = project.UngroupedDevicesGroup.Devices
                    .CreateWithItem(pepperlDevice.Type, finalName, finalDeviceName);
                if (device == null)
                {
                    logger("Create Pepperl returned null");
                    return null;
                }
                var modules = GetMasterModules(pepperlDevice);
                int nextInputAddress = pepperlDevice.StartAddress;
                int nextOutputAddress = pepperlDevice.StartAddress;
                int slot10NextAddress = pepperlDevice.StartAddress + 900;
                logger($"Master {pepperlDevice.Name}: StartAddress z CSV {pepperlDevice.StartAddress}");
                logger(settings != null && settings.Mode == PepperlAddressMode.BlockPerSlot32
                    ? $"Master {pepperlDevice.Name}: adresacja blokowa slot1..10 co 32 bajty I/Q, slot1 od {pepperlDevice.StartAddress}"
                    : settings != null && settings.Mode == PepperlAddressMode.BlockPerDevice
                    ? $"Master {pepperlDevice.Name}: adresacja blokowa slot1..9 co 100, slot10 od {slot10NextAddress}"
                    : $"Master {pepperlDevice.Name}: adresacja sekwencyjna");
                for (int i = 0; i < modules.Count; i++)
                {
                    try
                    {
                        int slotNumber = i + 1;
                        string rawName = modules[i].Name?.Trim();
                        if (string.IsNullOrWhiteSpace(rawName))
                            rawName = $"SLOT_{slotNumber}";
                        rawName = rawName
                            .Replace(".", "_")
                            .Replace(",", "_")
                            .Replace(" ", "_");
                        string moduleType = modules[i].Type;
                        string plugType;
                        if (string.IsNullOrWhiteSpace(moduleType) || moduleType == "0")
                        {
                            plugType = PepperlEmptyModuleType;
                        }
                        else
                        {
                            if (!moduleType.Contains("/M/ID_"))
                            {
                                logger($"Slot {slotNumber} pominięty – nieprawidłowy Type: {moduleType}");
                                continue;
                            }
                            plugType = moduleType;
                        }
                        DeviceItem pluggedItem = device.DeviceItems[0].PlugNew(
                            plugType,
                            rawName,
                            slotNumber
                        );
                        int addrIndex = i + 2;
                        bool isEmptyModule = IsEmptyModule(modules[i], plugType);
                        ModuleAddressingProfile profile = ResolveModuleAddressingProfile(plugType, isEmptyModule);
                        var slotRoot = device.DeviceItems.Count > addrIndex ? device.DeviceItems[addrIndex] : null;
                        var addressItems = CollectAddressItems(pluggedItem, slotRoot);
                        if (addressItems.Count > 0)
                        {
                            int resolvedAddress = ResolvePlannedAddress(
                                pepperlDevice.StartAddress,
                                slotNumber,
                                ref nextInputAddress,
                                ref nextOutputAddress,
                                ref slot10NextAddress,
                                settings,
                                profile,
                                isEmptyModule);

                            if (resolvedAddress < 0)
                            {
                                logger($"Slot {slotNumber} | {modules[i].Name} -> EMPTY, bez adresacji");
                            }
                            else
                            {
                                int commonStartAddress = resolvedAddress;
                                foreach (var addressItem in addressItems)
                                {
                                    for (int addressIdx = 0; addressIdx < addressItem.Addresses.Count; addressIdx++)
                                        addressItem.Addresses[addressIdx].SetAttribute("StartAddress", commonStartAddress);
                                }

                                AdvanceNextAddress(
                                    commonStartAddress,
                                    slotNumber,
                                    ref nextInputAddress,
                                    ref nextOutputAddress,
                                    ref slot10NextAddress,
                                    settings,
                                    profile);

                                logger($"Slot {slotNumber} | {modules[i].Name} -> adres {commonStartAddress}, typ {profile.Direction}, I {profile.InputSize}, Q {profile.OutputSize}");
                            }
                        }
                        else
                        {
                            logger($"Address mapping skipped for slot {slotNumber} (index {addrIndex}) – no addresses found in slot tree");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger($"Plug failed slot {i + 1} | {ex.Message}");
                    }
                }
                var netIf = GetNetworkInterface(device);
                if (netIf == null) { logger("No network interface"); return device; }
                var subnet = project.Subnets.FirstOrDefault(s => s.Name.Equals(pepperlDevice.SubnetName, StringComparison.OrdinalIgnoreCase));
                if (subnet == null) { logger("Subnet not found: " + pepperlDevice.SubnetName); return device; }
                var node = netIf.Nodes.FirstOrDefault();
                if (node == null) { logger("No node"); return device; }
                node.ConnectToSubnet(subnet);
                node.SetAttribute("Address", pepperlDevice.IpAddress);
                var ioSystem = subnet.IoSystems.FirstOrDefault(a =>
                    a.HwIdentifiers?[0]?.HwIdentifierControllers?[0]?.GetAttribute("Name")?.Equals(pepperlDevice.PlcName) == true);
                if (ioSystem == null)
                    ioSystem = subnet.IoSystems.FirstOrDefault();
                if (ioSystem == null)
                {
                    logger("IoSystem not found for PLC: " + pepperlDevice.PlcName);
                    return device;
                }
                var ioConnector = netIf.IoConnectors.FirstOrDefault();
                if (ioConnector == null) { logger("No IoConnector"); return device; }
                ioConnector.ConnectToIoSystem(ioSystem);
                ioConnector.SetAttribute("PnDeviceNumber", pepperlDevice.ProfinetNumber);
                return device;
            }
            catch (Exception ex)
            {
                logger("AddPepperl ex: " + ex.Message);
                return null;
            }
        }

        private const string Hik3050Type = "GSD:GSDML-V2.34-HIKVISION-MV-20210719.XML/DAP/DAP 3";

        private static IEnumerable<Device> HikProjectDevices(ProjectBase project)
        {
            return project.Devices.Concat(project.UngroupedDevicesGroup.Devices)
                .Concat(HikGroupDevices(project.DeviceGroups)).Distinct();
        }

        private static IEnumerable<Device> HikGroupDevices(IEnumerable<DeviceUserGroup> groups)
        {
            foreach (var group in groups)
            {
                foreach (var device in group.Devices) yield return device;
                foreach (var device in HikGroupDevices(group.Groups)) yield return device;
            }
        }

        // Half-open byte intervals; I and Q have independent address spaces.
        private static int HikNextFreeAddress(int start, int length, List<Tuple<int, int>> occupied)
        {
            foreach (var range in occupied.OrderBy(r => r.Item1))
            {
                if (range.Item2 <= start) continue;
                if (checked(start + length) <= range.Item1) break;
                start = range.Item2;
            }
            return start;
        }

        private static int HikRequiredInt(Dictionary<string, string> row, params string[] names)
        {
            string value = CsvString(row, names);
            int number;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < 0)
                throw new FormatException("hik 3050: brak lub błędna wartość " + names[0] + ": " + value);
            return number;
        }

        private static List<KeyenceCameraDevice> ReadHik3050Csv(string csvPath, out int nextInput, out int nextOutput)
        {
            nextInput = 0; nextOutput = 0;
            var cameras = new List<KeyenceCameraDevice>();
            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) return cameras;
            char separator = lines[0].Contains(";") ? ';' : lines[0].Contains("\t") ? '\t' : ',';
            var headers = lines[0].Split(separator).Select(h => h.Trim().Trim('"', '\uFEFF')).ToArray();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var values = lines[i].Split(separator).Select(v => v.Trim().Trim('"')).ToArray();
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < headers.Length && j < values.Length; j++) row[headers[j]] = values[j];
                string name = row.ContainsKey("Name") ? CsvString(row, "Name") : CsvString(row, "device name", "DeviceName", "StationName");
                if (string.IsNullOrWhiteSpace(name) || name == "0") break;
                if (cameras.Count == 0)
                {
                    nextInput = HikRequiredInt(row, "StartAddress", "start address i", "InputStartAddress", "StartAddressI", "I");
                    nextOutput = HikRequiredInt(row, "StartAddress", "start address q", "OutputStartAddress", "StartAddressQ", "Q");
                }
                string ip = CsvString(row, "ip address", "IpAddress", "IP");
                System.Net.IPAddress parsedIp;
                if (!System.Net.IPAddress.TryParse(ip, out parsedIp) ||
                    parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    throw new FormatException("hik 3050: błędny IP w wierszu " + (i + 1));
                int pn = HikRequiredInt(row, "profi number", "ProfiNumber", "ProfinetNumber", "PnDeviceNumber", "ProfiAddress");
                if (pn == 0) throw new FormatException("hik 3050: profi number musi być większy od zera, wiersz " + (i + 1));
                string deviceName = CsvString(row, "DeviceName", "device name", "StationName");
                string type = CsvString(row, "Type");
                cameras.Add(new KeyenceCameraDevice
                {
                    Name = name, DeviceName = string.IsNullOrWhiteSpace(deviceName) || deviceName == "0" ? name + "_DEV" : deviceName,
                    Type = string.IsNullOrWhiteSpace(type) || type == "0" ? Hik3050Type : type, IpAddress = ip,
                    ProfinetNumber = pn, SubnetName = CsvString(row, "SubnetName", "Subnet"),
                    PlcName = CsvString(row, "PlcName", "PLC"), GroupName = CsvString(row, "GroupName", "Group")
                });
            }

            return cameras;
        }

        private static void ImportHik3050(ProjectBase project, string csvPath, Action<string> logger)
        {
            int nextInput, nextOutput;
            var cameras = ReadHik3050Csv(csvPath, out nextInput, out nextOutput);
            if (cameras.Count == 0) { logger("hik 3050: CSV nie zawiera urządzeń"); return; }

            var inputRanges = new List<Tuple<int, int>>();
            var outputRanges = new List<Tuple<int, int>>();
            foreach (var existing in HikProjectDevices(project))
                foreach (var item in CollectAddressItems(existing.DeviceItems.ToArray()))
                    foreach (Address address in item.Addresses)
                    {
                        var direction = address.IoType;
                        if (direction != AddressIoType.Input && direction != AddressIoType.Output) continue;
                        int start = address.StartAddress;
                        int length = AddressBitLengthToBytes(address.Length);
                        if (start < 0) throw new InvalidOperationException("hik 3050: nie można odczytać zajętego adresu: " + item.Name);
                        (direction == AddressIoType.Input ? inputRanges : outputRanges)
                            .Add(Tuple.Create(start, checked(start + length)));
                    }

            foreach (var camera in cameras)
            {
                var devices = HikProjectDevices(project).ToList();
                if (devices.Any(d => d.Name.Equals(camera.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceItems.Any(item => item.Name.Equals(camera.Name, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException("hik 3050: urządzenie już istnieje: " + camera.Name);
                var targetDevices = project.UngroupedDevicesGroup.Devices;
                if (!string.IsNullOrWhiteSpace(camera.GroupName) && camera.GroupName != "0")
                {
                    var group = project.DeviceGroups.Find(camera.GroupName) ?? project.DeviceGroups.Create(camera.GroupName);
                    targetDevices = group.Devices;
                }
                Device device = targetDevices.CreateWithItem(camera.Type, camera.Name, camera.DeviceName);
                if (device == null) throw new InvalidOperationException("Nie utworzono hik 3050: " + camera.Name);
                try
                {
                    ConfigureKeyenceCameraNetwork(device, project.Subnets, camera, logger, true);
                    int count = 0;
                    foreach (var item in CollectAddressItems(device.DeviceItems.ToArray()))
                        foreach (Address address in item.Addresses)
                        {
                            var direction = address.IoType;
                            if (direction != AddressIoType.Input && direction != AddressIoType.Output) continue;
                            int length = AddressBitLengthToBytes(address.Length);
                            bool input = direction == AddressIoType.Input;
                            var ranges = input ? inputRanges : outputRanges;
                            int start = HikNextFreeAddress(input ? nextInput : nextOutput, length, ranges);
                            int end = checked(start + length);
                            address.SetAttribute("StartAddress", start);
                            if (address.StartAddress != start) throw new InvalidOperationException("TIA nie przyjęło adresu " + start);
                            ranges.Add(Tuple.Create(start, end));
                            if (input) nextInput = end; else nextOutput = end;
                            logger($"hik 3050 {camera.Name}: {(input ? "I" : "Q")}{start}, {length} bajtów");
                            count++;
                        }
                    if (count == 0)
                        throw new InvalidOperationException("DAP nie wystawił adresów I/Q. Wymagane identyfikatory modułów z GSDML.");
                    logger("Dodano hik 3050: " + camera.Name);
                }
                catch (Exception ex)
                {
                    logger("hik 3050 " + camera.Name + ": " + ex.Message + " Import zatrzymany.");
                    // Remove only the device created by this failed iteration.
                    try { device.Delete(); }
                    catch (Exception cleanup) { logger("Nie usunięto niepełnego urządzenia: " + cleanup.Message); }
                    throw;
                }
            }
        }

        private static Device AddKeyenceCameraDevice(Project project, KeyenceCameraDevice camera, Action<string> logger)
        {
            try
            {
                string finalName = ResolveUniqueDeviceItemName(project.Devices, camera.Name);
                string finalDeviceName = string.IsNullOrWhiteSpace(camera.DeviceName) || camera.DeviceName.Trim() == "0"
                    ? finalName + "_DEV"
                    : camera.DeviceName.Trim();
                string type = string.IsNullOrWhiteSpace(camera.Type) || camera.Type.Trim() == "0"
                    ? KeyenceCameraType
                    : camera.Type.Trim();

                Device device = project.UngroupedDevicesGroup.Devices.CreateWithItem(type, finalName, finalDeviceName);
                if (device == null)
                {
                    logger("Create Keyence camera returned null");
                    return null;
                }

                ConfigureKeyenceCameraAddresses(device, camera, logger);
                ConfigureKeyenceCameraNetwork(device, project.Subnets, camera, logger);

                return device;
            }
            catch (Exception ex)
            {
                logger("AddKeyenceCamera ex: " + ex.Message);
                return null;
            }
        }

        private static Device AddKeyenceCameraDevice(Siemens.Engineering.Multiuser.MultiuserProject project, KeyenceCameraDevice camera, Action<string> logger)
        {
            try
            {
                string finalName = ResolveUniqueDeviceItemName(project.Devices, camera.Name);
                string finalDeviceName = string.IsNullOrWhiteSpace(camera.DeviceName) || camera.DeviceName.Trim() == "0"
                    ? finalName + "_DEV"
                    : camera.DeviceName.Trim();
                string type = string.IsNullOrWhiteSpace(camera.Type) || camera.Type.Trim() == "0"
                    ? KeyenceCameraType
                    : camera.Type.Trim();

                Device device = project.UngroupedDevicesGroup.Devices.CreateWithItem(type, finalName, finalDeviceName);
                if (device == null)
                {
                    logger("Create Keyence camera returned null");
                    return null;
                }

                ConfigureKeyenceCameraAddresses(device, camera, logger);
                ConfigureKeyenceCameraNetwork(device, project.Subnets, camera, logger);

                return device;
            }
            catch (Exception ex)
            {
                logger("AddKeyenceCamera ex: " + ex.Message);
                return null;
            }
        }

        private static string ResolveUniqueDeviceItemName(IEnumerable<Device> devices, string requestedName)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "SR_X1H3HX" : requestedName.Trim();
            string finalName = baseName;
            int suffix = 1;

            while (devices.Any(d => d.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase)))
            {
                finalName = baseName + "_" + suffix;
                suffix++;
            }

            return finalName;
        }

        private static void ConfigureKeyenceCameraAddresses(Device device, KeyenceCameraDevice camera, Action<string> logger)
        {
            int nextInputAddress = camera.StartAddress;
            int nextOutputAddress = camera.StartAddress;

            var roots = new List<DeviceItem>();
            foreach (var item in device.DeviceItems)
                roots.Add(item);

            var addressItems = CollectAddressItems(roots.ToArray());
            int inputCount = 0;
            int outputCount = 0;

            foreach (var addressItem in addressItems)
            {
                for (int addressIdx = 0; addressIdx < addressItem.Addresses.Count; addressIdx++)
                {
                    var address = addressItem.Addresses[addressIdx];
                    var ioType = SafeAddressIoType(address);
                    int length = SafeAddressLength(address);

                    if (ioType == AddressIoType.Input)
                    {
                        int start = nextInputAddress;
                        address.SetAttribute("StartAddress", start);
                        logger($"Camera {camera.Name}: I{start} length {length} byte(s)");
                        nextInputAddress += length;
                        inputCount++;
                    }
                    else if (ioType == AddressIoType.Output)
                    {
                        int start = nextOutputAddress;
                        address.SetAttribute("StartAddress", start);
                        logger($"Camera {camera.Name}: Q{start} length {length} byte(s)");
                        nextOutputAddress += length;
                        outputCount++;
                    }
                }
            }

            if (inputCount == 0 && outputCount == 0)
                logger("Camera " + camera.Name + ": DAP nie wystawił adresów I/Q. Jeśli TIA wymaga modułów, potrzebne będą ich TypeIdentifier z GSDML.");
        }

        private static void ConfigureKeyenceCameraNetwork(Device device, IEnumerable<Subnet> subnets, KeyenceCameraDevice camera, Action<string> logger, bool required = false)
        {
            var netIf = GetNetworkInterface(device);
            if (netIf == null)
            {
                if (required) throw new InvalidOperationException("hik 3050: nie można skonfigurować sieci dla " + camera.Name);
                logger("Camera " + camera.Name + ": No network interface");
                return;
            }

            var subnet = string.IsNullOrWhiteSpace(camera.SubnetName)
                ? subnets.FirstOrDefault()
                : subnets.FirstOrDefault(s => s.Name.Equals(camera.SubnetName, StringComparison.OrdinalIgnoreCase));
            if (subnet == null)
            {
                if (required) throw new InvalidOperationException("hik 3050: nie można skonfigurować sieci dla " + camera.Name);
                logger("Camera " + camera.Name + ": Subnet not found: " + camera.SubnetName);
                return;
            }

            var node = netIf.Nodes.FirstOrDefault();
            if (node == null)
            {
                if (required) throw new InvalidOperationException("hik 3050: nie można skonfigurować sieci dla " + camera.Name);
                logger("Camera " + camera.Name + ": No node");
                return;
            }

            node.ConnectToSubnet(subnet);
            if (!string.IsNullOrWhiteSpace(camera.IpAddress))
                node.SetAttribute("Address", camera.IpAddress);

            var ioSystem = subnet.IoSystems.FirstOrDefault(a =>
                a.HwIdentifiers?[0]?.HwIdentifierControllers?[0]?.GetAttribute("Name")?.Equals(camera.PlcName) == true);
            if (ioSystem == null)
                ioSystem = subnet.IoSystems.FirstOrDefault();
            if (ioSystem == null)
            {
                if (required) throw new InvalidOperationException("hik 3050: nie można skonfigurować sieci dla " + camera.Name);
                logger("Camera " + camera.Name + ": IoSystem not found for PLC: " + camera.PlcName);
                return;
            }

            var ioConnector = netIf.IoConnectors.FirstOrDefault();
            if (ioConnector == null)
            {
                if (required) throw new InvalidOperationException("hik 3050: nie można skonfigurować sieci dla " + camera.Name);
                logger("Camera " + camera.Name + ": No IoConnector");
                return;
            }

            ioConnector.ConnectToIoSystem(ioSystem);
            if (camera.ProfinetNumber > 0)
                ioConnector.SetAttribute("PnDeviceNumber", camera.ProfinetNumber);
        }

        private static AddressIoType SafeAddressIoType(Address address)
        {
            try { return address.IoType; } catch { }

            try
            {
                string raw = address.GetAttribute("IoType")?.ToString() ?? "";
                if (raw.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AddressIoType.Output;
                if (raw.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AddressIoType.Input;
            }
            catch { }

            return AddressIoType.None;
        }

        private static int SafeAddressLength(Address address)
        {
            try { return AddressBitLengthToBytes(Convert.ToInt32(address.Length, CultureInfo.InvariantCulture)); } catch { }
            try { return AddressBitLengthToBytes(Convert.ToInt32(address.GetAttribute("Length"), CultureInfo.InvariantCulture)); } catch { }
            return 1;
        }

        private static int AddressBitLengthToBytes(int bitLength)
        {
            return Math.Max(1, (bitLength + 7) / 8);
        }

        private static int SafeAddressStart(Address address)
        {
            try { return Convert.ToInt32(address.StartAddress, CultureInfo.InvariantCulture); } catch { }
            try { return Convert.ToInt32(address.GetAttribute("StartAddress"), CultureInfo.InvariantCulture); } catch { }
            return -1;
        }

        private static IEnumerable<string> ParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return Array.Empty<string>();

            var result = new List<string>();
            bool inQuotes = false;
            var current = "";

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(current.Trim());
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            result.Add(current.Trim());
            return result;
        }

        private static IEnumerable<Dictionary<string, string>> GetDevicesFromCSV(string deviceType, string path, Action<string> logger)
        {
            var res = new List<Dictionary<string, string>>();
            if (!File.Exists(path)) { logger("CSV not found: " + path); return res; }
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return res;
            var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var vals = ParseCsvLine(lines[i]).ToArray();
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < headers.Length && j < vals.Length; j++) dict[headers[j]] = vals[j];
                res.Add(dict);
            }
            return res;
        }

        private static LenzeDevice MapToLenze(Dictionary<string, string> d)
        {
            var x = new LenzeDevice();
            typeof(LenzeDevice).GetProperties().ToList().ForEach(p => { if (d.ContainsKey(p.Name)) { try { var v = d[p.Name]; if (p.PropertyType == typeof(int)) { if (int.TryParse(v, out int iv)) p.SetValue(x, iv); } else p.SetValue(x, v); } catch { } } });
            return x;
        }
        private static PepperlDevice MapToPepperl(Dictionary<string, string> d)
        {
            var x = new PepperlDevice();
            typeof(PepperlDevice).GetProperties().ToList().ForEach(p => { if (d.ContainsKey(p.Name)) { try { var v = d[p.Name]; if (p.PropertyType == typeof(int)) { if (int.TryParse(v, out int iv)) p.SetValue(x, iv); } else p.SetValue(x, v); } catch { } } });
            x.Type = string.IsNullOrWhiteSpace(x.Type) ? PepperlMasterType : NormalizePepperlType(x.Type);
            return x;
        }

        private static string NormalizePepperlType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return type;

            string value = type.Trim();
            if (string.Equals(value, "G20", StringComparison.OrdinalIgnoreCase))
                return PepperlG20ModuleType;

            var match = Regex.Match(value,
                @"^GSD:GSDML-V[^/]+-PEPPERL\+FUCHS-ICE38IOL(?:-[^/]+)?\.XML(?<suffix>/.*)$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return value;

            string suffix = match.Groups["suffix"].Value;
            if (Regex.IsMatch(suffix, @"^/M/ID_Mod_G20(?:_|$)", RegexOptions.IgnoreCase))
                return PepperlG20ModuleType;

            return suffix.StartsWith("/DAP/", StringComparison.OrdinalIgnoreCase)
                ? PepperlMasterType
                : PepperlGsdType + suffix;
        }

        private static KeyenceCameraDevice MapToKeyenceCamera(Dictionary<string, string> d)
        {
            var x = new KeyenceCameraDevice();

            x.Name = CsvString(d, "Name", "CameraName");
            x.DeviceName = CsvString(d, "DeviceName", "StationName");
            x.Type = CsvString(d, "Type", "Gsdml", "GSDML");
            x.IpAddress = CsvString(d, "IpAddress", "IPAddress", "IP");
            x.SubnetName = CsvString(d, "SubnetName", "Subnet");
            x.PlcName = CsvString(d, "PlcName", "PLC", "CpuName");
            x.GroupName = CsvString(d, "GroupName", "Group");
            x.ProfinetNumber = CsvInt(d, 0, "ProfinetNumber", "ProfiAddress", "ProfinetAddress", "PnDeviceNumber", "PnNumber", "DeviceNumber");
            x.StartAddress = CsvInt(d, 0, "StartAddress", "AddressStart", "BaseAddress");
            x.InputStartAddress = x.StartAddress;
            x.OutputStartAddress = x.StartAddress;

            if (string.IsNullOrWhiteSpace(x.Type))
                x.Type = KeyenceCameraType;

            return x;
        }

        private static string CsvString(Dictionary<string, string> row, params string[] names)
        {
            foreach (string name in names)
            {
                if (row.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static int CsvInt(Dictionary<string, string> row, int defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (!row.TryGetValue(name, out string value) || string.IsNullOrWhiteSpace(value))
                    continue;

                value = value.Trim();
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    return parsed;
                if (int.TryParse(value, out parsed))
                    return parsed;
            }

            return defaultValue;
        }

        private static NetworkInterface GetNetworkInterface(Device device)
        {
            foreach (var di in device.DeviceItems)
            {
                var svc = di as Siemens.Engineering.IEngineeringServiceProvider;
                if (svc != null)
                {
                    try { var net = svc.GetService<NetworkInterface>(); if (net != null) return net; } catch { }
                }
                var nested = GetNetworkInterfaceFromDeviceItem(di);
                if (nested != null) return nested;
            }
            return null;
        }

        private static NetworkInterface GetNetworkInterfaceFromDeviceItem(DeviceItem item)
        {
            if (item == null) return null;
            var svc = item as Siemens.Engineering.IEngineeringServiceProvider;
            if (svc != null)
            {
                try { var net = svc.GetService<NetworkInterface>(); if (net != null) return net; } catch { }
            }
            foreach (var ch in item.DeviceItems) { var n = GetNetworkInterfaceFromDeviceItem(ch); if (n != null) return n; }
            return null;
        }

        private static Device AddLenzeDevice(Project project, LenzeDevice hwDevice, Action<string> logger)
        {
            try
            {
                Device device = project.UngroupedDevicesGroup.Devices.CreateWithItem(hwDevice.Type, hwDevice.Name, hwDevice.DeviceName);
                if (device == null) { logger("Create device returned null"); return null; }

                // addresses - best effort
                try
                {
                    if (device.DeviceItems.Count > 7)
                    {
                        device.DeviceItems[2].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 4);
                        device.DeviceItems[3].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 0);
                        device.DeviceItems[4].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 0);
                        device.DeviceItems[5].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 2);
                        device.DeviceItems[6].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 2);
                        device.DeviceItems[7].DeviceItems[0].Addresses[0].SetAttribute("StartAddress", hwDevice.StartAddress + 4);
                    }
                }
                catch { logger("Lenze address mapping failed"); }

                var netIf = GetNetworkInterface(device);
                if (netIf == null) { logger("No network interface on device"); return device; }

                var proj = project as Project;
                var subnet = proj?.Subnets.FirstOrDefault(s => s.Name.Equals(hwDevice.SubnetName, StringComparison.OrdinalIgnoreCase));
                if (subnet == null) { logger("Subnet not found: " + hwDevice.SubnetName); return device; }

                var node = netIf.Nodes.FirstOrDefault();
                if (node == null) { logger("No node"); return device; }
                node.ConnectToSubnet(subnet);
                node.SetAttribute("Address", hwDevice.IpAddress);

                var ioSystem = subnet.IoSystems.FirstOrDefault(a => a.HwIdentifiers?[0]?.HwIdentifierControllers?[0]?.GetAttribute("Name")?.Equals(hwDevice.PlcName) == true);
                if (ioSystem == null) ioSystem = subnet.IoSystems.FirstOrDefault();
                if (ioSystem == null) { logger("IoSystem not found"); return device; }

                var ioConnector = netIf.IoConnectors.FirstOrDefault();
                if (ioConnector == null) { logger("No IoConnector"); return device; }
                ioConnector.ConnectToIoSystem(ioSystem);
                ioConnector.SetAttribute("PnDeviceNumber", hwDevice.ProfinetNumber);

                return device;
            }
            catch (Exception ex) { logger("AddLenze ex: " + ex.Message); return null; }
        }
        public class MasterModule
        {
            public string Name { get; set; }
            public string Type { get; set; }

            public MasterModule(string name, string type)
            {
                Name = name;
                Type = type;
            }
        }

        private static List<MasterModule> GetMasterModules(PepperlDevice pepperlDevice)
        {
            var masterModules = new List<MasterModule>();

            string masterName = pepperlDevice.Name; // ✅ potrzebne do budowy nazw EMPTY

            for (int i = 1; i <= 10; i++)
            {
                string nameProp = $"Slot{i}Name";
                string typeProp = $"Slot{i}Type";

                var np = pepperlDevice.GetType().GetProperty(nameProp);
                var tp = pepperlDevice.GetType().GetProperty(typeProp);

                string name = np?.GetValue(pepperlDevice)?.ToString()?.Trim();
                string type = NormalizePepperlType(tp?.GetValue(pepperlDevice)?.ToString());

                // ✅ SLOT NAME = "0" OR EMPTY → AUTOMAT NAZWY, ALE SLOT JEST DODAWANY
                if (name == "0" || string.IsNullOrWhiteSpace(name))
                {
                    name = $"{masterName}_{i}_EMPTY";
                }



                masterModules.Add(new MasterModule(name, type));
            }

            return masterModules;
        }




        private static PepperlAddressMode ParsePepperlAddressMode(string mode)
        {
            if (string.Equals(mode, "BlockPerDevice", StringComparison.OrdinalIgnoreCase))
                return PepperlAddressMode.BlockPerDevice;

            if (string.Equals(mode, "BlockPerSlot32", StringComparison.OrdinalIgnoreCase))
                return PepperlAddressMode.BlockPerSlot32;

            return PepperlAddressMode.Sequential;
        }

        private static bool IsEmptyModule(MasterModule module, string plugType)
        {
            string name = module?.Name ?? "";
            string type = module?.Type ?? "";
            string resolvedType = plugType ?? "";

            return string.IsNullOrWhiteSpace(type)
                || type == "0"
                || name.IndexOf("_EMPTY", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("IOLinkEmpty", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedType.IndexOf("IOLinkEmpty", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(resolvedType, PepperlEmptyModuleType, StringComparison.OrdinalIgnoreCase);
        }

        private static List<DeviceItem> CollectAddressItems(params DeviceItem[] roots)
        {
            var result = new List<DeviceItem>();

            foreach (var root in roots)
                CollectAddressItemsRecursive(root, result);

            return result;
        }

        private static void CollectAddressItemsRecursive(DeviceItem item, List<DeviceItem> result)
        {
            if (item == null)
                return;

            if (item.Addresses != null && item.Addresses.Count > 0 && !result.Contains(item))
                result.Add(item);

            foreach (var child in item.DeviceItems)
                CollectAddressItemsRecursive(child, result);
        }

        private static int ResolvePlannedAddress(
            int masterStartAddress,
            int slotNumber,
            ref int nextInputAddress,
            ref int nextOutputAddress,
            ref int slot10NextAddress,
            PepperlAddressingSettings settings,
            ModuleAddressingProfile profile,
            bool isEmptyModule)
        {
            if (isEmptyModule)
                return -1;

            if (settings != null && settings.Mode == PepperlAddressMode.BlockPerSlot32)
            {
                if (profile.MaxSize > 32)
                    throw new InvalidOperationException("Moduł przekracza rezerwację 32 bajtów I/Q dla slotu " + slotNumber);

                return checked(masterStartAddress + ((slotNumber - 1) * 32));
            }

            if (settings != null && settings.Mode == PepperlAddressMode.BlockPerDevice)
            {
                if (slotNumber == 10)
                    return slot10NextAddress;

                return masterStartAddress + (slotNumber * 100);
            }

            if (profile.Direction == ModuleIoDirection.InputOutput)
                return Math.Max(nextInputAddress, nextOutputAddress);

            if (profile.Direction == ModuleIoDirection.Output)
                return nextOutputAddress;

            return nextInputAddress;
        }

        private static void AdvanceNextAddress(
            int commonStartAddress,
            int slotNumber,
            ref int nextInputAddress,
            ref int nextOutputAddress,
            ref int slot10NextAddress,
            PepperlAddressingSettings settings,
            ModuleAddressingProfile profile)
        {
            if (settings != null && settings.Mode == PepperlAddressMode.BlockPerSlot32)
                return;

            if (settings != null && settings.Mode == PepperlAddressMode.BlockPerDevice)
            {
                if (slotNumber == 9 || slotNumber == 10)
                    slot10NextAddress = commonStartAddress + profile.MaxSize;

                return;
            }

            if (profile.InputSize > 0)
                nextInputAddress = commonStartAddress + profile.InputSize;

            if (profile.OutputSize > 0)
                nextOutputAddress = commonStartAddress + profile.OutputSize;
        }

        private static Device AddPepperlDevice(Project project, PepperlDevice pepperlDevice, PepperlAddressingSettings settings, PepperlAddressingState state, Action<string> logger)
        {


            if (pepperlDevice.Name == "0")
            {
                logger("AddPepperl ABORT — Name == 0");
                return null;
            }

            try
            {
                // zabezpieczenie na kolizję nazw urządzenia
                string finalName = pepperlDevice.Name;
                int suffix = 1;
                while (project.UngroupedDevicesGroup.Devices.Any(d => d.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase)))
                {
                    finalName = pepperlDevice.Name + "_" + suffix;
                    suffix++;
                }

                string finalDeviceName = finalName + "_DEV";

                Device device = project.UngroupedDevicesGroup.Devices
                    .CreateWithItem(pepperlDevice.Type, finalName, finalDeviceName);

                if (device == null)
                {
                    logger("Create Pepperl returned null");
                    return null;
                }

                var modules = GetMasterModules(pepperlDevice);
                int nextInputAddress = pepperlDevice.StartAddress;
                int nextOutputAddress = pepperlDevice.StartAddress;
                int slot10NextAddress = pepperlDevice.StartAddress + 900;
                logger($"Master {pepperlDevice.Name}: StartAddress z CSV {pepperlDevice.StartAddress}");
                logger(settings != null && settings.Mode == PepperlAddressMode.BlockPerSlot32
                    ? $"Master {pepperlDevice.Name}: adresacja blokowa slot1..10 co 32 bajty I/Q, slot1 od {pepperlDevice.StartAddress}"
                    : settings != null && settings.Mode == PepperlAddressMode.BlockPerDevice
                    ? $"Master {pepperlDevice.Name}: adresacja blokowa slot1..9 co 100, slot10 od {slot10NextAddress}"
                    : $"Master {pepperlDevice.Name}: adresacja sekwencyjna");



                for (int i = 0; i < modules.Count; i++)
                {
                    try
                    {
                        int slotNumber = i + 1;

                        string rawName = modules[i].Name?.Trim();
                        if (string.IsNullOrWhiteSpace(rawName))
                            rawName = $"SLOT_{slotNumber}";

                        rawName = rawName
                            .Replace(".", "_")
                            .Replace(",", "_")
                            .Replace(" ", "_");

                        string moduleType = modules[i].Type;

                        // ✅ JEŚLI PUSTE → AUTOMATYCZNIE WSTAWIAMY IOLinkEmpty
                        string plugType;
                        if (string.IsNullOrWhiteSpace(moduleType) || moduleType == "0")
                        {
                            plugType = PepperlEmptyModuleType;
                        }
                        else
                        {
                            // ✅ TYLKO PEŁNE GSD Z CSV
                            if (!moduleType.Contains("/M/ID_"))
                            {
                                logger($"Slot {slotNumber} pominięty – nieprawidłowy Type: {moduleType}");
                                continue;
                            }

                            plugType = moduleType;
                        }

                        // ✅ PLUG
                        DeviceItem pluggedItem = device.DeviceItems[0].PlugNew(
                            plugType,
                            rawName,
                            slotNumber
                        );

                        // ✅ ADRESACJA TYLKO GDY SLOT FAKTYCZNIE ISTNIEJE (i+2)
                        int addrIndex = i + 2;
                        bool isEmptyModule = IsEmptyModule(modules[i], plugType);
                        ModuleAddressingProfile profile = ResolveModuleAddressingProfile(plugType, isEmptyModule);
                        var slotRoot = device.DeviceItems.Count > addrIndex ? device.DeviceItems[addrIndex] : null;
                        var addressItems = CollectAddressItems(pluggedItem, slotRoot);

                        if (addressItems.Count > 0)
                        {
                            int resolvedAddress = ResolvePlannedAddress(
                                pepperlDevice.StartAddress,
                                slotNumber,
                                ref nextInputAddress,
                                ref nextOutputAddress,
                                ref slot10NextAddress,
                                settings,
                                profile,
                                isEmptyModule);

                            if (resolvedAddress < 0)
                            {
                                logger($"Slot {slotNumber} | {modules[i].Name} -> EMPTY, bez adresacji");
                            }
                            else
                            {
                                int commonStartAddress = resolvedAddress;
                                foreach (var addressItem in addressItems)
                                {
                                    for (int addressIdx = 0; addressIdx < addressItem.Addresses.Count; addressIdx++)
                                        addressItem.Addresses[addressIdx].SetAttribute("StartAddress", commonStartAddress);
                                }

                                AdvanceNextAddress(
                                    commonStartAddress,
                                    slotNumber,
                                    ref nextInputAddress,
                                    ref nextOutputAddress,
                                    ref slot10NextAddress,
                                    settings,
                                    profile);

                                logger($"Slot {slotNumber} | {modules[i].Name} -> adres {commonStartAddress}, typ {profile.Direction}, I {profile.InputSize}, Q {profile.OutputSize}");
                            }
                        }
                        else
                        {
                            logger($"Address mapping skipped for slot {slotNumber} (index {addrIndex}) – no addresses found in slot tree");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger($"Plug failed slot {i + 1} | {ex.Message}");
                    }
                }



                // ===== SIEĆ =====
                var netIf = GetNetworkInterface(device);
                if (netIf == null) { logger("No network interface"); return device; }

                var proj = project as Project;
                var subnet = proj?.Subnets.FirstOrDefault(s => s.Name.Equals(pepperlDevice.SubnetName, StringComparison.OrdinalIgnoreCase));
                if (subnet == null) { logger("Subnet not found: " + pepperlDevice.SubnetName); return device; }

                var node = netIf.Nodes.FirstOrDefault();
                if (node == null) { logger("No node"); return device; }

                node.ConnectToSubnet(subnet);
                node.SetAttribute("Address", pepperlDevice.IpAddress);

                var ioSystem = subnet.IoSystems.FirstOrDefault(a =>
                    a.HwIdentifiers?[0]?.HwIdentifierControllers?[0]?.GetAttribute("Name")?.Equals(pepperlDevice.PlcName) == true);

                if (ioSystem == null)
                    ioSystem = subnet.IoSystems.FirstOrDefault();

                if (ioSystem == null)
                {
                    logger("IoSystem not found for PLC: " + pepperlDevice.PlcName);
                    return device;
                }

                var ioConnector = netIf.IoConnectors.FirstOrDefault();
                if (ioConnector == null) { logger("No IoConnector"); return device; }

                ioConnector.ConnectToIoSystem(ioSystem);
                ioConnector.SetAttribute("PnDeviceNumber", pepperlDevice.ProfinetNumber);

                return device;
            }
            catch (Exception ex)
            {
                logger("AddPepperl ex: " + ex.Message);
                return null;
            }
        }



        private static int ByteSize(string gsdml)
        {
            string value = gsdml ?? "";

            var tokenPatterns = new[]
            {
                @"(\d+)\s*(?:bytes?|byte)",
                @"(?:inout|inputoutput|input|output|di|dq|ai|aq|in|out)\s*(\d+)",
                @"(\d+)\s*(?:ch|channel|channels)"
            };

            foreach (string pattern in tokenPatterns)
            {
                var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                string numericGroup = match.Groups
                    .Cast<Group>()
                    .Skip(1)
                    .FirstOrDefault(g => g.Success && g.Value.All(char.IsDigit))
                    ?.Value;

                if (int.TryParse(numericGroup, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    return parsed;
            }

            return 1;
        }

        private static ModuleIoDirection ResolveModuleIoDirection(string gsdml)
        {
            string value = (gsdml ?? "").ToLowerInvariant();

            bool hasInputOutput =
                value.Contains("inout")
                || value.Contains("inputoutput")
                || value.Contains("in/out")
                || value.Contains("in_out")
                || value.Contains("in-out");

            if (hasInputOutput)
                return ModuleIoDirection.InputOutput;

            bool hasOutput =
                value.Contains("output")
                || Regex.IsMatch(value, @"(?:^|[_\-/])(?:dq|aq|out)(?:\d|[_\-/]|$)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(value, @"iolinkout\d+", RegexOptions.IgnoreCase);

            if (hasOutput)
                return ModuleIoDirection.Output;

            bool hasInput =
                value.Contains("input")
                || Regex.IsMatch(value, @"(?:^|[_\-/])(?:di|ai|in)(?:\d|[_\-/]|$)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(value, @"iolinkin\d+", RegexOptions.IgnoreCase);

            if (hasInput)
                return ModuleIoDirection.Input;

            return ModuleIoDirection.Input;
        }

        private static ModuleAddressingProfile ResolveModuleAddressingProfile(string gsdml, bool isEmptyModule)
        {
            if (isEmptyModule)
            {
                return new ModuleAddressingProfile
                {
                    Direction = ModuleIoDirection.None,
                    InputSize = 0,
                    OutputSize = 0
                };
            }

            string value = (gsdml ?? "").ToLowerInvariant();

            if (value.Contains("g20"))
            {
                return new ModuleAddressingProfile
                {
                    Direction = ModuleIoDirection.InputOutput,
                    InputSize = 8,
                    OutputSize = 18
                };
            }

            if (value.Contains("power"))
            {
                return new ModuleAddressingProfile
                {
                    Direction = ModuleIoDirection.InputOutput,
                    InputSize = 2,
                    OutputSize = 1
                };
            }

            ModuleIoDirection direction = ResolveModuleIoDirection(gsdml);
            int size = Math.Max(1, ByteSize(gsdml));

            if (direction == ModuleIoDirection.Output)
            {
                return new ModuleAddressingProfile
                {
                    Direction = direction,
                    InputSize = 0,
                    OutputSize = size
                };
            }

            if (direction == ModuleIoDirection.InputOutput)
            {
                return new ModuleAddressingProfile
                {
                    Direction = direction,
                    InputSize = size,
                    OutputSize = size
                };
            }

            return new ModuleAddressingProfile
            {
                Direction = ModuleIoDirection.Input,
                InputSize = size,
                OutputSize = 0
            };
        }

        // ==========================================================================================================
        // PLC Security for MultiuserProject
        // ==========================================================================================================
        /// <summary>
        /// Apply PLC security defaults to a project already loaded in a Multiuser session.  This overload operates
        /// directly on the Siemens.Engineering.Multiuser.MultiuserProject instance and mirrors the behaviour of
        /// ApplyPlcSecurityDefaults(string projectPath, ...) without requiring a .apXX file path.  All CPU creation
        /// and selection logic is retained.
        /// </summary>
        /// <param name="project">The MultiuserProject to modify.</param>
        /// <param name="catalogNumberNormalized">
        ///     If provided, a new CPU will be created from the hardware catalog with this normalized type identifier
        ///     or order number.  Otherwise, security will be applied to an existing CPU in the project.  If exactly
        ///     one CPU exists in the project it will be used automatically; otherwise a target must be specified via
        ///     the targetExistingCpu parameter (null in this overload triggers automatic selection when only one CPU
        ///     is present).
        /// </param>
        /// <param name="logger">Delegate for logging progress or errors.</param>
        public static void ApplyPlcSecurityDefaults(
            Siemens.Engineering.Multiuser.MultiuserProject project,
            string catalogNumberNormalized,
            Action<string> logger)
        {
            ApplyPlcSecurity(project, catalogNumberNormalized, null, logger);
        }

        public static void ApplyPlcSecurityDefaults(
            Siemens.Engineering.Multiuser.MultiuserProject project,
            string catalogNumberNormalized,
            CpuCandidate targetExistingCpu,
            Action<string> logger)
        {
            ApplyPlcSecurity(project, catalogNumberNormalized, targetExistingCpu, logger);
        }

        /// <summary>
        /// Core implementation for applying PLC security in a MultiuserProject.  This method performs the same
        /// operations as the Project-based ApplyPlcSecurity method: optionally creates a new CPU from the hardware
        /// catalog, or selects an existing CPU, then applies security defaults to it.
        /// </summary>
        private static void ApplyPlcSecurity(
            Siemens.Engineering.Multiuser.MultiuserProject project,
            string catalogNumberNormalized,
            CpuCandidate targetExistingCpu,
            Action<string> logger)
        {
            if (tia == null)
                OpenTIA("attach", logger);
            if (tia == null)
                throw new InvalidOperationException("TIA is not available.");
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            DeviceItem cpu;
            if (!string.IsNullOrWhiteSpace(catalogNumberNormalized))
            {
                // create a new CPU/device from hardware catalog in the Multiuser project
                cpu = CreateCpuFromCatalogNumber(project, catalogNumberNormalized, logger);
            }
            else
            {
                // select existing CPU from project
                if (targetExistingCpu == null)
                {
                    var candidates = GetCpuCandidates(project, logger);
                    if (candidates.Count == 1)
                    {
                        targetExistingCpu = candidates[0];
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "No target CPU selected. Load CPUs and pick one from the list, or specify a catalog number.");
                    }
                }
                cpu = FindCpuBySelection(project, targetExistingCpu);
            }
            // First configure project-level Security settings when the Multiuser API exposes UMAC.
            try
            {
                ConfigureUserAndRolePreset(project, cpu, logger);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"UMAC configuration skipped: {ex.Message}");
            }

            // Apply default security settings
            ApplySecurityDefaults(cpu, logger);

            // UMAC may not be available in every MU context; ConfigureUserAndRolePreset logs and returns in that case.
        }

        /// <summary>
        /// Enumerate all CPU candidates in a MultiuserProject.  Each CPU candidate describes a CPU by device name,
        /// CPU name, order number, firmware version and normalized type identifier.
        /// </summary>
        public static List<CpuCandidate> GetCpuCandidates(Siemens.Engineering.Multiuser.MultiuserProject project, Action<string> logger)
        {
            var list = new List<CpuCandidate>();
            if (project == null)
                return list;
            foreach (var dev in project.Devices)
            {
                foreach (var di in dev.DeviceItems.SelectMany(Flatten))
                {
                    if (di.Classification != DeviceItemClassifications.CPU)
                        continue;
                    string order = SafeGetStringAttr(di, "OrderNumber");
                    string fw = SafeGetStringAttr(di, "FirmwareVersion");
                    string typeId = SafeGetStringAttr(di, "TypeIdentifierNormalized");
                    string name = SafeGetStringAttr(di, "Name");
                    list.Add(new CpuCandidate
                    {
                        DeviceName = dev.Name,
                        CpuName = string.IsNullOrWhiteSpace(name) ? di.Name : name,
                        OrderNumber = order,
                        FirmwareVersion = fw,
                        TypeIdentifierNormalized = typeId
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// Find a CPU DeviceItem in a MultiuserProject based on a CpuCandidate selection.  The matching rules
        /// replicate those of the Project-based version: exact name match is preferred; otherwise a match on
        /// TypeIdentifierNormalized is allowed.
        /// </summary>
        private static DeviceItem FindCpuBySelection(
            Siemens.Engineering.Multiuser.MultiuserProject project,
            CpuCandidate sel)
        {
            foreach (var dev in project.Devices)
            {
                if (!string.Equals(dev.Name, sel.DeviceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var di in dev.DeviceItems.SelectMany(Flatten))
                {
                    if (di.Classification != DeviceItemClassifications.CPU)
                        continue;
                    string name = SafeGetStringAttr(di, "Name");
                    if (!string.IsNullOrWhiteSpace(name) && string.Equals(name, sel.CpuName, StringComparison.OrdinalIgnoreCase))
                        return di;
                    if (string.Equals(di.Name, sel.CpuName, StringComparison.OrdinalIgnoreCase))
                        return di;
                    string typeId = SafeGetStringAttr(di, "TypeIdentifierNormalized");
                    if (!string.IsNullOrWhiteSpace(sel.TypeIdentifierNormalized) &&
                        string.Equals(typeId, sel.TypeIdentifierNormalized, StringComparison.OrdinalIgnoreCase))
                        return di;
                }
            }
            throw new InvalidOperationException("Selected CPU not found in project.");
        }

        /// <summary>
        /// Create a new CPU device from the hardware catalog in a MultiuserProject.  This replicates the logic of
        /// CreateCpuFromCatalogNumber(Project) but operates directly on a MultiuserProject instance.
        /// </summary>
        private static DeviceItem CreateCpuFromCatalogNumber(
            Siemens.Engineering.Multiuser.MultiuserProject project,
            string catalogNumberNormalized,
            Action<string> logger)
        {
            var input = (catalogNumberNormalized ?? string.Empty).Trim().Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Catalog number is empty.");
            string typeId;
            if (input.StartsWith("OrderNumber:", StringComparison.OrdinalIgnoreCase))
            {
                typeId = input;
            }
            else
            {
                typeId = ResolveTypeIdFromHardwareCatalog(input, logger);
            }
            string tmpDeviceName = "station_" + DateTime.Now.ToString("HHmmss");
            string tmpItemName = "PLC_" + DateTime.Now.ToString("HHmmss");
            Device device = project.UngroupedDevicesGroup.Devices.CreateWithItem(typeId, tmpItemName, tmpDeviceName);
            logger?.Invoke($"Created device from catalog: {typeId} -> {device.Name}");
            var cpu = device.DeviceItems.SelectMany(Flatten).FirstOrDefault(di => di.Classification == DeviceItemClassifications.CPU);
            if (cpu == null)
                throw new InvalidOperationException("Created device but CPU item not found.");
            return cpu;
        }

        /// <summary>
        /// Configure a preset of users and roles for PLC access using the UMAC (User Management & Access Control) service.
        /// This method activates the anonymous user and creates two additional users with custom roles. The roles are
        /// assigned runtime and function rights using reflection to support multiple TIA versions. If UMAC is not
        /// available for the given project, the method logs a warning and returns.  Any errors thrown by the UMAC API
        /// are caught and logged to avoid breaking the primary security workflow.
        /// </summary>
        /// <param name="project">The TIA Portal project object containing the CPU.</param>
        /// <param name="cpu">The CPU DeviceItem that has just had defaults applied.</param>
        /// <param name="logger">Delegate for logging progress and warnings.</param>
        private static void ConfigureUserAndRolePreset(object project, DeviceItem cpu, Action<string> logger)
        {
            if (project == null)
                return;

            // Try to obtain the UMAC service via reflection.  If unavailable, skip configuration.
            object umac = null;
            try
            {
                var getServiceMethod = project.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetService" &&
                                         m.IsGenericMethodDefinition &&
                                         m.GetParameters().Length == 0);
                if (getServiceMethod != null)
                {
                    var securityAssembly = typeof(Project).Assembly;
                    var umacType = securityAssembly.GetType("Siemens.Engineering.Umac.UmacConfigurator", false);
                    if (umacType != null)
                    {
                        umac = getServiceMethod.MakeGenericMethod(umacType).Invoke(project, null);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"UMAC service lookup failed: {ex.Message}");
            }
            if (umac == null)
            {
                logger?.Invoke("UMAC service not available on this project; skipping user preset.");
                return;
            }

            // Local helper: get a property value via reflection
            object GetProp(object obj, string propName)
            {
                if (obj == null) return null;
                var prop = obj.GetType().GetProperty(propName) ??
                           obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                return prop?.GetValue(obj);
            }

            // Local helper: invoke a parameterless method via reflection
            void InvokeMethod(object obj, string methodName)
            {
                if (obj == null) return;
                var m = obj.GetType().GetMethod(methodName, Type.EmptyTypes);
                m?.Invoke(obj, null);
            }

            // Activate anonymous user if possible
            try
            {
                InvokeMethod(umac, "ActivateAnonymousUser");
            }
            catch
            {
                // ignore
            }

            // Get the anonymous user object
            var anonymous = GetProp(umac, "AnonymousUser");

            // Get custom roles collection and users collection via reflection
            var customRolesCollection = GetProp(umac, "CustomRoles");
            var usersCollection = GetProp(umac, "ProjectUsers") ?? GetProp(umac, "Users") ?? GetProp(umac, "GlobalUsers");

            // Helper to find or create a role by name.  Uses reflection; comment is optional.
            object FindOrCreateRole(string name)
            {
                if (customRolesCollection == null) return null;
                // Try to iterate and find by Name property
                try
                {
                    foreach (var roleObj in (System.Collections.IEnumerable)customRolesCollection)
                    {
                        var n = GetProp(roleObj, "Name") as string;
                        if (!string.IsNullOrWhiteSpace(n) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                            return roleObj;
                    }
                }
                catch
                {
                    // ignore enumeration issues
                }
                // Create method might have signature Create(string name, string comment) or Create(string name)
                try
                {
                    var create = customRolesCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(string) });
                    if (create != null)
                    {
                        return create.Invoke(customRolesCollection, new object[] { name, string.Empty });
                    }
                }
                catch { }
                try
                {
                    var create = customRolesCollection.GetType().GetMethod("Create", new[] { typeof(string) });
                    if (create != null)
                    {
                        return create.Invoke(customRolesCollection, new object[] { name });
                    }
                }
                catch { }
                return null;
            }

            // Helper to find or create a user by name.  Uses reflection; password required.
            object FindOrCreateUser(string name, string password)
            {
                if (usersCollection == null) return null;
                try
                {
                    foreach (var userObj in (System.Collections.IEnumerable)usersCollection)
                    {
                        var n = GetProp(userObj, "Name") as string;
                        if (!string.IsNullOrWhiteSpace(n) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                            return userObj;
                    }
                }
                catch
                {
                }

                if (string.Equals(name, "Amsort_user", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        foreach (var userObj in (System.Collections.IEnumerable)usersCollection)
                        {
                            var n = GetProp(userObj, "Name") as string;
                            if (!string.IsNullOrWhiteSpace(n) && string.Equals(n, "Amosrt_user", StringComparison.OrdinalIgnoreCase))
                            {
                                var setName = userObj.GetType().GetMethod("SetName", new[] { typeof(string) });
                                setName?.Invoke(userObj, new object[] { "Amsort_user" });
                                logger?.Invoke("UMAC user renamed: Amosrt_user -> Amsort_user.");
                                return userObj;
                            }
                        }
                    }
                    catch { }
                }

                // Create method might have (string name, string password) or (string name, SecureString password) signature
                try
                {
                    var create = usersCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(string) });
                    if (create != null)
                    {
                        return create.Invoke(usersCollection, new object[] { name, password });
                    }
                }
                catch { }
                try
                {
                    var create = usersCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(SecureString) });
                    if (create != null)
                    {
                        using (var securePassword = ToSecureString(password))
                        {
                            return create.Invoke(usersCollection, new object[] { name, securePassword });
                        }
                    }
                }
                catch { }
                try
                {
                    var create = usersCollection.GetType().GetMethods()
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "Create") return false;
                            var ps = m.GetParameters();
                            return ps.Length >= 1 &&
                                   ps[0].ParameterType == typeof(string) &&
                                   ps.All(p => p.ParameterType == typeof(string) || p.ParameterType == typeof(SecureString) || p.IsOptional);
                        });
                    if (create != null)
                    {
                        var args = create.GetParameters()
                            .Select((p, index) =>
                            {
                                if (index == 0) return (object)name;
                                if (p.ParameterType == typeof(string)) return password;
                                if (p.ParameterType == typeof(SecureString)) return ToSecureString(password);
                                return Type.Missing;
                            })
                            .ToArray();
                        return create.Invoke(usersCollection, args);
                    }
                }
                catch { }
                return null;
            }

            SecureString ToSecureString(string value)
            {
                var secure = new SecureString();
                foreach (var ch in value ?? string.Empty)
                    secure.AppendChar(ch);
                secure.MakeReadOnly();
                return secure;
            }

            string NormalizeRightName(string value)
            {
                return Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
            }

            bool NameMatches(string candidate, string[] aliases)
            {
                var normalized = NormalizeRightName(candidate);
                return aliases.Any(alias =>
                {
                    var normalizedAlias = NormalizeRightName(alias);
                    if (normalized == normalizedAlias || normalized.Contains(normalizedAlias))
                        return true;

                    var aliasParts = Regex.Split(alias ?? string.Empty, "[^A-Za-z0-9]+")
                        .Where(part => part.Length > 1)
                        .Select(NormalizeRightName)
                        .ToArray();
                    return aliasParts.Length > 0 && aliasParts.All(normalized.Contains);
                });
            }

            bool TrySetBoolRight(object obj, string[] aliases, HashSet<object> visited, int depth)
            {
                if (obj == null || depth < 0 || visited.Contains(obj))
                    return false;

                visited.Add(obj);
                bool changed = false;
                var type = obj.GetType();

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        if (prop.PropertyType == typeof(bool) && prop.CanWrite && NameMatches(prop.Name, aliases))
                        {
                            prop.SetValue(obj, true);
                            changed = true;
                        }
                    }
                    catch { }
                }

                foreach (var methodName in new[] { "Set", "Enable", "Grant" })
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == methodName && m.GetParameters().Length >= 1))
                    {
                        foreach (var alias in aliases)
                        {
                            try
                            {
                                var ps = method.GetParameters();
                                object[] args = ps.Select((p, index) =>
                                {
                                    if (index == 0 && p.ParameterType == typeof(string)) return (object)alias;
                                    if (p.ParameterType == typeof(bool)) return true;
                                    if (p.IsOptional) return Type.Missing;
                                    return null;
                                }).ToArray();

                                if (args.All(a => a != null))
                                {
                                    method.Invoke(obj, args);
                                    changed = true;
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
                {
                    foreach (var item in enumerable)
                        changed |= TrySetBoolRight(item, aliases, visited, depth - 1);
                }

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.PropertyType == typeof(string) || prop.PropertyType.IsValueType)
                        continue;

                    try
                    {
                        var child = prop.GetValue(obj);
                        if (child == null)
                            continue;

                        changed |= TrySetBoolRight(child, aliases, visited, depth - 1);
                    }
                    catch { }
                }

                return changed;
            }

            object GetService(object source, string serviceTypeName)
            {
                if (source == null) return null;

                try
                {
                    var serviceType = typeof(Project).Assembly.GetType(serviceTypeName, false);
                    if (serviceType == null) return null;

                    var getService = source.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetService" &&
                                             m.IsGenericMethodDefinition &&
                                             m.GetParameters().Length == 0);
                    return getService?.MakeGenericMethod(serviceType).Invoke(source, null);
                }
                catch
                {
                    return null;
                }
            }

            object FindRight(object rightsCollection, string[] aliases)
            {
                if (rightsCollection == null) return null;

                foreach (var alias in aliases)
                {
                    try
                    {
                        var find = rightsCollection.GetType().GetMethod("Find", new[] { typeof(string) });
                        var found = find?.Invoke(rightsCollection, new object[] { alias });
                        if (found != null)
                            return found;
                    }
                    catch { }
                }

                try
                {
                    foreach (var right in (System.Collections.IEnumerable)rightsCollection)
                    {
                        var name = GetProp(right, "Name") as string;
                        var id = GetProp(right, "Identifier") as string;
                        var group = GetProp(right, "Group") as string;
                        var combined = $"{group} {name} {id}";

                        if (NameMatches(name, aliases) ||
                            NameMatches(id, aliases) ||
                            aliases.Any(alias => NormalizeRightName(combined).Contains(NormalizeRightName(alias))))
                        {
                            return right;
                        }
                    }
                }
                catch { }

                return null;
            }

            object GetUmacDevice()
            {
                var current = (object)cpu;
                var visited = new HashSet<object>();

                while (current != null && !visited.Contains(current))
                {
                    visited.Add(current);

                    var service = GetService(current, "Siemens.Engineering.Umac.UmacDevice");
                    if (service != null)
                    {
                        logger?.Invoke("UMAC: using device rights from " + current.GetType().Name + ".");
                        return service;
                    }

                    current = GetProp(current, "Parent");
                }

                return null;
            }

            string RightLabel(object right)
            {
                if (right == null) return string.Empty;
                var group = GetProp(right, "Group") as string;
                var name = GetProp(right, "Name") as string;
                var id = GetProp(right, "Identifier") as string;
                return $"{group}/{name} [{id}]".Trim();
            }

            bool AssignDeviceRight(object roleObj, object umacDevice, object right)
            {
                if (roleObj == null || umacDevice == null || right == null) return false;

                try
                {
                    var assigned = roleObj.GetType().GetMethod("GetAssignedDeviceFunctionRights")?
                        .Invoke(roleObj, new[] { umacDevice });
                    if (assigned is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var assignedRight in enumerable)
                        {
                            var assignedId = GetProp(assignedRight, "Identifier") as string;
                            var rightId = GetProp(right, "Identifier") as string;
                            if (!string.IsNullOrWhiteSpace(assignedId) &&
                                string.Equals(assignedId, rightId, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    var assign = roleObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "AssignDeviceFunctionRight" && m.GetParameters().Length == 2);
                    if (assign == null)
                    {
                        logger?.Invoke("UMAC: AssignDeviceFunctionRight method not found on role " + (GetProp(roleObj, "Name") as string));
                        return false;
                    }

                    assign.Invoke(roleObj, new[] { umacDevice, right });
                    logger?.Invoke("UMAC: assigned right " + RightLabel(right) + " to role " + (GetProp(roleObj, "Name") as string) + ".");
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Invoke("UMAC: assigning right failed: " + RightLabel(right) + " -> " + (ex.InnerException?.Message ?? ex.Message));
                    return false;
                }
            }

            void AssignDeviceRights(object roleObj, object umacDevice, params string[][] rightAliases)
            {
                if (roleObj == null || umacDevice == null) return;

                var availableRights = GetProp(umacDevice, "AvailableDeviceFunctionRights");
                if (availableRights == null)
                {
                    logger?.Invoke("UMAC: CPU does not expose available device function rights.");
                    return;
                }

                foreach (var aliases in rightAliases)
                {
                    var right = FindRight(availableRights, aliases);
                    if (right == null)
                    {
                        logger?.Invoke("UMAC: device right not found: " + aliases[0]);
                        try
                        {
                            var knownRights = ((System.Collections.IEnumerable)availableRights)
                                .Cast<object>()
                                .Select(RightLabel)
                                .Where(label => !string.IsNullOrWhiteSpace(label))
                                .Take(30);
                            logger?.Invoke("UMAC: available rights sample: " + string.Join("; ", knownRights));
                        }
                        catch { }
                        continue;
                    }

                    AssignDeviceRight(roleObj, umacDevice, right);
                }
            }

            // Helper: assign a role to a user (roleObj to userObj) via Roles property
            void AssignRoleToUser(object userObj, object roleObj)
            {
                if (userObj == null || roleObj == null) return;
                try
                {
                    var rolesList = GetProp(userObj, "Roles");
                    if (rolesList != null)
                    {
                        var containsMethod = rolesList.GetType().GetMethod("Contains", new[] { roleObj.GetType() });
                        bool already = false;
                        if (containsMethod != null)
                        {
                            already = (bool)containsMethod.Invoke(rolesList, new object[] { roleObj });
                        }
                        else
                        {
                            var roleIdentifier = GetProp(roleObj, "Identifier") as string;
                            var roleName = GetProp(roleObj, "Name") as string;
                            foreach (var assignedRole in (System.Collections.IEnumerable)rolesList)
                            {
                                var assignedIdentifier = GetProp(assignedRole, "Identifier") as string;
                                var assignedName = GetProp(assignedRole, "Name") as string;
                                if ((!string.IsNullOrWhiteSpace(roleIdentifier) &&
                                     string.Equals(roleIdentifier, assignedIdentifier, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrWhiteSpace(roleName) &&
                                     string.Equals(roleName, assignedName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    already = true;
                                    break;
                                }
                            }
                        }
                        if (!already)
                        {
                            var addMethod = rolesList.GetType().GetMethod("Add", new[] { roleObj.GetType() }) ??
                                            rolesList.GetType().GetMethods().FirstOrDefault(m =>
                                            {
                                                if (m.Name != "Add") return false;
                                                var ps = m.GetParameters();
                                                return ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(roleObj.GetType());
                                            });
                            if (addMethod != null)
                            {
                                addMethod.Invoke(rolesList, new object[] { roleObj });
                            }
                        }
                    }
                }
                catch { }
            }

            void ActivateUser(object userObj)
            {
                if (userObj == null) return;
                try
                {
                    var activate = userObj.GetType().GetMethod("Activate", Type.EmptyTypes);
                    activate?.Invoke(userObj, null);
                }
                catch { }
            }

            var plcUmacDevice = GetUmacDevice();
            if (plcUmacDevice == null)
            {
                logger?.Invoke("UMAC: PLC_1 Function rights service not available on selected CPU.");
            }

            // Configure Anonymous role and rights
            try
            {
                var anonRole = FindOrCreateRole("Anonymous");
                if (anonRole != null)
                {
                    AssignDeviceRights(
                        anonRole,
                        plcUmacDevice,
                        new[] { "FullAccess", "Full access" });
                    AssignRoleToUser(anonymous, anonRole);
                    logger?.Invoke("UMAC role ready: Anonymous -> anonymous user.");
                }
            }
            catch (Exception ex) { logger?.Invoke($"UMAC Anonymous role skipped: {ex.Message}"); }

            // Configure Amsort role and user
            try
            {
                var amsRole = FindOrCreateRole("Amsort");
                if (amsRole != null)
                {
                    AssignDeviceRights(
                        amsRole,
                        plcUmacDevice,
                        new[] { "FullAccess", "Full access" },
                        new[] { "FullAccessIncludingFailSafe", "FullAccessIncludingFailsafe", "Full access including fail-safe", "Full access including failsafe", "Full access incl fail-safe", "Full access incl failsafe", "FullAccessInclFailSafe", "FullAccessInclFailsafe" });
                    var amsPassword = Environment.GetEnvironmentVariable("TIA_AMSORT_USER_PASSWORD");
                    if (string.IsNullOrWhiteSpace(amsPassword))
                        throw new InvalidOperationException("Set TIA_AMSORT_USER_PASSWORD before creating Amsort_user.");
                    var amsUser = FindOrCreateUser("Amsort_user", amsPassword);
                    ActivateUser(amsUser);
                    AssignRoleToUser(amsUser, amsRole);
                    logger?.Invoke("UMAC role ready: Amsort -> Amsort_user.");
                }
            }
            catch (Exception ex) { logger?.Invoke($"UMAC Amsort role/user skipped: {ex.Message}"); }

            // Configure OPC UA role and user
            try
            {
                var opcRole = FindOrCreateRole("OPC_UA");
                if (opcRole != null)
                {
                    AssignDeviceRights(
                        opcRole,
                        plcUmacDevice,
                        new[] { "OpcUaServerAccess", "OpcUAServerAccess", "OPCUAServerAccess", "OPC UA server access" },
                        new[] { "ManageCertificates", "Manage certificates" });
                    var opcPassword = Environment.GetEnvironmentVariable("TIA_OPC_UA_USER_PASSWORD");
                    if (string.IsNullOrWhiteSpace(opcPassword))
                        throw new InvalidOperationException("Set TIA_OPC_UA_USER_PASSWORD before creating OPC_UA_USER.");
                    var opcUser = FindOrCreateUser("OPC_UA_USER", opcPassword);
                    ActivateUser(opcUser);
                    AssignRoleToUser(opcUser, opcRole);
                    logger?.Invoke("UMAC role ready: OPC_UA -> OPC_UA_USER.");
                }
            }
            catch (Exception ex) { logger?.Invoke($"UMAC OPC_UA role/user skipped: {ex.Message}"); }

            // After UMAC changes, reapply PUT/GET attribute in case the change requires re-setting
            try
            {
                SetBoolAttr(cpu, "ProtectionEnablePutGetCommunication", true, logger);
            }
            catch
            {
                // ignore
            }
        }
    }
}
