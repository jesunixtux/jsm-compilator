using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Steamworks;

namespace JumpfallJsmCompilator;

public sealed class MainForm : Form
{
    private const uint AppId = 4053390;
    private const string PackageType = "jumpfall_map";
    private const int PackageFormatVersion = 1;
    private const string LocalTestWorkshopId = "local_test";
    private const string WorkshopAssetFolderName = "assetlocal";
    private const string BackgroundImagesFolderName = "backgroundimg";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> KnownPieceIds = new(StringComparer.Ordinal)
    {
        "box_ground",
        "checkpoint",
        "apple",
        "orb_jump"
    };

    private static readonly HashSet<string> KnownTriggerIds = new(StringComparer.Ordinal)
    {
        "limit_map",
        "deathzone",
        "changelevel",
        "finish_level"
    };

    private readonly TextBox txtMap = new();
    private readonly TextBox txtAssets = new();
    private readonly TextBox txtTitle = new();
    private readonly TextBox txtDescription = new();
    private readonly TextBox txtPreview = new();
    private readonly TextBox txtWorkshopId = new();
    private readonly TextBox txtChangeNote = new();
    private readonly TextBox txtLog = new();
    private readonly ComboBox cboVisibility = new();
    private readonly ProgressBar progress = new();
    private readonly Label lblProgress = new();
    private readonly Label lblSteam = new();

    private string lastJsmPath = string.Empty;
    private string currentMetaPath = string.Empty;
    private PublishedFileId_t currentFileId;
    private UGCUpdateHandle_t currentUpdate;
    private CallResult<CreateItemResult_t>? createCallback;
    private CallResult<SubmitItemUpdateResult_t>? submitCallback;
    private System.Windows.Forms.Timer? steamTimer;
    private System.Windows.Forms.Timer? progressTimer;
    private bool steamInitialized;

    public MainForm()
    {
        Text = "Jumpfall JSM Compilator";
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        TryInitializeSteam();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        progressTimer?.Stop();
        steamTimer?.Stop();

        if (steamInitialized)
        {
            try { SteamAPI.Shutdown(); }
            catch { }
        }

        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 14,
            Padding = new Padding(10),
            AutoScroll = true
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        for (int i = 0; i < 14; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(root);

        AddHeader(root);
        AddMapRows(root);
        AddWorkshopRows(root);
        AddActionRows(root);
        AddProgressRows(root);
        AddLogRows(root);
    }

    private void AddHeader(TableLayoutPanel root)
    {
        var title = new Label
        {
            Text = "Jumpfall JSM Compilator",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        };
        root.SetColumnSpan(title, 4);
        root.Controls.Add(title, 0, 0);

        lblSteam.Text = $"Steam App ID: {AppId} (fixed in code)";
        lblSteam.AutoSize = true;
        lblSteam.TextAlign = ContentAlignment.MiddleRight;
        root.SetColumnSpan(lblSteam, 2);
        root.Controls.Add(lblSteam, 4, 0);
    }

    private void AddMapRows(TableLayoutPanel root)
    {
        root.Controls.Add(new Label { Text = "Map (.jmap/.jfue):", AutoSize = true }, 0, 1);
        txtMap.Dock = DockStyle.Fill;
        root.SetColumnSpan(txtMap, 4);
        root.Controls.Add(txtMap, 1, 1);

        var browseMap = new Button { Text = "Examine..." };
        browseMap.Click += BrowseMap;
        root.Controls.Add(browseMap, 5, 1);

        root.Controls.Add(new Label { Text = "Assets folder:", AutoSize = true }, 0, 2);
        txtAssets.Dock = DockStyle.Fill;
        txtAssets.PlaceholderText = @"Auto: Documents\jumpfall\levels\assetslocal";
        root.SetColumnSpan(txtAssets, 4);
        root.Controls.Add(txtAssets, 1, 2);

        var browseAssets = new Button { Text = "Examine..." };
        browseAssets.Click += BrowseAssets;
        root.Controls.Add(browseAssets, 5, 2);

        root.Controls.Add(new Label { Text = "Title:", AutoSize = true }, 0, 3);
        txtTitle.Dock = DockStyle.Fill;
        root.SetColumnSpan(txtTitle, 5);
        root.Controls.Add(txtTitle, 1, 3);

        root.Controls.Add(new Label { Text = "Description:", AutoSize = true }, 0, 4);
        txtDescription.Dock = DockStyle.Fill;
        txtDescription.Multiline = true;
        txtDescription.Height = 80;
        txtDescription.ScrollBars = ScrollBars.Vertical;
        root.SetColumnSpan(txtDescription, 5);
        root.Controls.Add(txtDescription, 1, 4);

        root.Controls.Add(new Label { Text = "Preview:", AutoSize = true }, 0, 5);
        txtPreview.Dock = DockStyle.Fill;
        root.SetColumnSpan(txtPreview, 4);
        root.Controls.Add(txtPreview, 1, 5);

        var browsePreview = new Button { Text = "Examine..." };
        browsePreview.Click += BrowsePreview;
        root.Controls.Add(browsePreview, 5, 5);
    }

    private void AddWorkshopRows(TableLayoutPanel root)
    {
        root.Controls.Add(new Label { Text = "Workshop Item ID:", AutoSize = true }, 0, 6);
        txtWorkshopId.Dock = DockStyle.Fill;
        txtWorkshopId.PlaceholderText = "Auto for new uploads";
        root.SetColumnSpan(txtWorkshopId, 2);
        root.Controls.Add(txtWorkshopId, 1, 6);

        root.Controls.Add(new Label { Text = "Visibility:", AutoSize = true }, 3, 6);
        cboVisibility.Dock = DockStyle.Fill;
        cboVisibility.DropDownStyle = ComboBoxStyle.DropDownList;
        cboVisibility.Items.AddRange(new[] { "Public", "FriendsOnly", "Private", "Unlisted" });
        cboVisibility.SelectedIndex = 2;
        root.SetColumnSpan(cboVisibility, 2);
        root.Controls.Add(cboVisibility, 4, 6);

        root.Controls.Add(new Label { Text = "Change note:", AutoSize = true }, 0, 7);
        txtChangeNote.Dock = DockStyle.Fill;
        txtChangeNote.Text = "Initial upload";
        root.SetColumnSpan(txtChangeNote, 5);
        root.Controls.Add(txtChangeNote, 1, 7);
    }

    private void AddActionRows(TableLayoutPanel root)
    {
        var loadMeta = new Button { Text = "Read metadata", Dock = DockStyle.Fill };
        loadMeta.Click += (_, _) => LoadMetadata();
        root.Controls.Add(loadMeta, 1, 8);

        var package = new Button { Text = "Package .jsm", Dock = DockStyle.Fill };
        package.Click += async (_, _) => await PackageLocalAsync();
        root.Controls.Add(package, 2, 8);

        var publish = new Button { Text = "Publish / Update Workshop", Dock = DockStyle.Fill };
        publish.Click += async (_, _) => await PublishOrUpdateAsync();
        root.SetColumnSpan(publish, 2);
        root.Controls.Add(publish, 3, 8);

        var openFolder = new Button { Text = "Open Folder", Dock = DockStyle.Fill };
        openFolder.Click += (_, _) => OpenLastFolder();
        root.Controls.Add(openFolder, 5, 8);
    }

    private void AddProgressRows(TableLayoutPanel root)
    {
        progress.Dock = DockStyle.Fill;
        progress.Height = 20;
        lblProgress.Text = "Progress: 0%";
        lblProgress.AutoSize = true;

        root.SetColumnSpan(progress, 5);
        root.Controls.Add(progress, 0, 9);
        root.Controls.Add(lblProgress, 5, 9);
    }

    private void AddLogRows(TableLayoutPanel root)
    {
        root.Controls.Add(new Label { Text = "Log:", AutoSize = true }, 0, 10);
        txtLog.Dock = DockStyle.Fill;
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.Height = 220;
        txtLog.ScrollBars = ScrollBars.Vertical;
        root.SetColumnSpan(txtLog, 6);
        root.Controls.Add(txtLog, 0, 11);
    }

    private void BrowseMap(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Jumpfall maps|*.jmap;*.jfue;*.json|All files|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(GetCreationsFolder()) ? GetCreationsFolder() : GetLevelsRoot()
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        txtMap.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(txtTitle.Text))
            txtTitle.Text = Path.GetFileNameWithoutExtension(dialog.FileName);

        AutoFillAssetsFolder(dialog.FileName);
        LoadMetadata();
    }

    private void BrowseAssets(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the assetlocal/assetslocal folder for this map.",
            InitialDirectory = Directory.Exists(GetLocalAssetsRoot()) ? GetLocalAssetsRoot() : GetLevelsRoot(),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            txtAssets.Text = dialog.SelectedPath;
    }

    private void BrowsePreview(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            txtPreview.Text = dialog.FileName;
    }

    private async Task PackageLocalAsync()
    {
        try
        {
            ResetProgress("Packaging...");
            string workshopId = string.IsNullOrWhiteSpace(txtWorkshopId.Text) ? LocalTestWorkshopId : txtWorkshopId.Text.Trim();
            JsmBuildResult result = await Task.Run(() => BuildJsmPackage(workshopId));
            lastJsmPath = result.JsmPath;
            SaveMetadata(result.WorkshopId);
            CompleteProgress();
            Log($"Generated .jsm: {result.JsmPath}");
            foreach (string warning in result.Warnings)
                Log("Warning: " + warning);
        }
        catch (Exception ex)
        {
            FailProgress();
            Log("Package error: " + ex.Message);
        }
    }

    private async Task PublishOrUpdateAsync()
    {
        if (!steamInitialized)
        {
            Log("Steam API is not initialized. Open Steam and restart this tool.");
            return;
        }

        try
        {
            string savedId = txtWorkshopId.Text.Trim();
            if (ulong.TryParse(savedId, out ulong existingId) && existingId > 0)
            {
                currentFileId = new PublishedFileId_t(existingId);
                JsmBuildResult result = await Task.Run(() => BuildJsmPackage(existingId.ToString()));
                lastJsmPath = result.JsmPath;
                SaveMetadata(existingId.ToString());
                StartUpload(isUpdate: true);
                return;
            }

            Log("Creating new Steam Workshop item...");
            createCallback = CallResult<CreateItemResult_t>.Create(OnItemCreated);
            SteamAPICall_t call = SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            createCallback.Set(call);
        }
        catch (Exception ex)
        {
            FailProgress();
            Log("Publish error: " + ex.Message);
        }
    }

    private void OnItemCreated(CreateItemResult_t result, bool failure)
    {
        if (failure || result.m_eResult != EResult.k_EResultOK)
        {
            Log("Error creating Workshop item: " + result.m_eResult);
            return;
        }

        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            Log("Steam requires accepting the Workshop legal agreement.");

        currentFileId = result.m_nPublishedFileId;
        string workshopId = currentFileId.m_PublishedFileId.ToString();
        txtWorkshopId.Text = workshopId;
        Log("Workshop item created: " + workshopId);

        try
        {
            JsmBuildResult build = BuildJsmPackage(workshopId);
            lastJsmPath = build.JsmPath;
            SaveMetadata(workshopId);
            StartUpload(isUpdate: false);
        }
        catch (Exception ex)
        {
            Log("Package error after item creation: " + ex.Message);
        }
    }

    private void StartUpload(bool isUpdate)
    {
        if (string.IsNullOrWhiteSpace(lastJsmPath) || !File.Exists(lastJsmPath))
            throw new InvalidOperationException("No .jsm package exists. Package the map first.");

        ResetProgress(isUpdate ? "Uploading update..." : "Uploading new item...");

        string uploadFolder = Path.Combine(Path.GetTempPath(), "jumpfall_jsm_upload", currentFileId.m_PublishedFileId.ToString());
        if (Directory.Exists(uploadFolder))
            Directory.Delete(uploadFolder, true);
        Directory.CreateDirectory(uploadFolder);
        File.Copy(lastJsmPath, Path.Combine(uploadFolder, Path.GetFileName(lastJsmPath)), true);

        currentUpdate = SteamUGC.StartItemUpdate(new AppId_t(AppId), currentFileId);
        SteamUGC.SetItemTitle(currentUpdate, SafeWorkshopText(txtTitle.Text, "Jumpfall Map"));
        SteamUGC.SetItemDescription(currentUpdate, txtDescription.Text.Trim());
        SteamUGC.SetItemContent(currentUpdate, uploadFolder);
        SteamUGC.SetItemVisibility(currentUpdate, GetSelectedVisibility());
        SteamUGC.SetItemTags(currentUpdate, new List<string> { "map", "level", "jsm" });

        if (!string.IsNullOrWhiteSpace(txtPreview.Text) && File.Exists(txtPreview.Text))
            SteamUGC.SetItemPreview(currentUpdate, txtPreview.Text);

        submitCallback = CallResult<SubmitItemUpdateResult_t>.Create(OnItemSubmitted);
        SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(
            currentUpdate,
            string.IsNullOrWhiteSpace(txtChangeNote.Text) ? (isUpdate ? "Map update" : "Initial upload") : txtChangeNote.Text.Trim());
        submitCallback.Set(submitCall);

        StartUploadProgressTimer();
    }

    private void OnItemSubmitted(SubmitItemUpdateResult_t result, bool failure)
    {
        progressTimer?.Stop();

        if (failure || result.m_eResult != EResult.k_EResultOK)
        {
            FailProgress();
            Log("Error uploading Workshop item: " + result.m_eResult);
            return;
        }

        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            Log("Steam requires accepting the Workshop legal agreement.");

        string id = result.m_nPublishedFileId.m_PublishedFileId.ToString();
        txtWorkshopId.Text = id;
        SaveMetadata(id);
        CompleteProgress();
        Log("Workshop upload completed. ID: " + id);
    }

    private JsmBuildResult BuildJsmPackage(string workshopId)
    {
        string sourcePath = txtMap.Text.Trim();
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("Select a valid .jmap or .jfue file.");

        string safeWorkshopId = SanitizeWorkshopId(workshopId);
        string mapName = SanitizeMapName(Path.GetFileNameWithoutExtension(sourcePath));
        string jfuePath = CompileToJfue(sourcePath, mapName);
        JsonNode mapData = ReadJsonNode(jfuePath);
        List<string> warnings = ValidateLevelData(mapData);
        string assetRootFolder = ResolveAssetsFolder(sourcePath);

        string workshopFolder = Path.Combine(GetWorkshopRoot(), safeWorkshopId);
        string workshopAssetFolder = Path.Combine(workshopFolder, WorkshopAssetFolderName);
        string backgroundAssetFolder = Path.Combine(workshopAssetFolder, BackgroundImagesFolderName);
        Directory.CreateDirectory(workshopFolder);
        Directory.CreateDirectory(workshopAssetFolder);
        Directory.CreateDirectory(backgroundAssetFolder);

        string mapFileName = Path.GetFileName(jfuePath);
        string targetMapPath = Path.Combine(workshopFolder, mapFileName);
        File.Copy(jfuePath, targetMapPath, true);

        List<CopiedAsset> copiedAssets = new();
        CopyAssetLocalFolder(assetRootFolder, workshopAssetFolder, copiedAssets, warnings);
        CopyReferencedAssets(mapData, backgroundAssetFolder, Path.GetDirectoryName(jfuePath) ?? string.Empty, assetRootFolder, copiedAssets, warnings);

        string jsmPath = Path.Combine(workshopFolder, Path.GetFileNameWithoutExtension(mapFileName) + ".jsm");
        if (File.Exists(jsmPath))
            File.Delete(jsmPath);

        using FileStream stream = new(jsmPath, FileMode.CreateNew, FileAccess.ReadWrite);
        using ZipArchive zip = new(stream, ZipArchiveMode.Create, false);
        zip.CreateEntryFromFile(targetMapPath, mapFileName, CompressionLevel.Optimal);
        zip.CreateEntry(WorkshopAssetFolderName + "/");
        zip.CreateEntry(WorkshopAssetFolderName + "/" + BackgroundImagesFolderName + "/");

        foreach (CopiedAsset asset in copiedAssets.DistinctBy(asset => asset.ZipPath, StringComparer.OrdinalIgnoreCase))
            zip.CreateEntryFromFile(asset.FullPath, asset.ZipPath.Replace('\\', '/'), CompressionLevel.Optimal);

        JsmManifest manifest = new()
        {
            MapFile = mapFileName,
            WorkshopId = safeWorkshopId,
            CreatedUtc = DateTime.UtcNow.ToString("O")
        };

        ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (StreamWriter writer = new(manifestEntry.Open()))
            writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

        return new JsmBuildResult(jsmPath, targetMapPath, safeWorkshopId, warnings);
    }

    private static string CompileToJfue(string sourcePath, string mapName)
    {
        string ext = Path.GetExtension(sourcePath);
        if (ext.Equals(".jfue", StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        if (!ext.Equals(".jmap", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Input must be .jmap, .jfue or .json.");

        JsonNode node = ReadJsonNode(sourcePath);
        JsonObject obj = node.AsObject();
        obj["version"] = Math.Max(obj["version"]?.GetValue<int>() ?? 6, 6);
        obj["gridSize"] ??= 1f;
        obj["spawnPoint"] ??= new JsonObject { ["x"] = 0f, ["y"] = 0f };
        obj["background"] ??= new JsonObject();
        obj["backgrounds"] ??= new JsonArray();
        obj["pieces"] ??= new JsonArray();
        obj["triggers"] ??= new JsonArray();

        string jfuePath = Path.Combine(GetCompilatorFolder(), mapName + ".jfue");
        Directory.CreateDirectory(Path.GetDirectoryName(jfuePath)!);
        File.WriteAllText(jfuePath, node.ToJsonString(JsonOptions));
        return jfuePath;
    }

    private static JsonNode ReadJsonNode(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node ?? throw new InvalidDataException("Invalid JSON: " + path);
    }

    private static List<string> ValidateLevelData(JsonNode data)
    {
        List<string> warnings = new();
        JsonObject obj = data.AsObject();

        if (obj["spawnPoint"] is null)
            warnings.Add("Map has no spawnPoint.");

        if (obj["pieces"] is JsonArray pieces)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                string id = pieces[i]?["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    warnings.Add($"Piece {i} has no id.");
                else if (!KnownPieceIds.Contains(id))
                    warnings.Add("Unknown piece id: " + id);
            }
        }
        else
        {
            throw new InvalidDataException("pieces must be an array.");
        }

        if (obj["triggers"] is JsonArray triggers)
        {
            for (int i = 0; i < triggers.Count; i++)
            {
                string id = triggers[i]?["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    warnings.Add($"Trigger {i} has no id.");
                else if (!KnownTriggerIds.Contains(id))
                    throw new InvalidDataException("Unknown trigger id: " + id);
            }
        }
        else
        {
            throw new InvalidDataException("triggers must be an array.");
        }

        return warnings;
    }

    private void AutoFillAssetsFolder(string mapPath)
    {
        if (!string.IsNullOrWhiteSpace(txtAssets.Text) && Directory.Exists(txtAssets.Text))
            return;

        string mapFolder = Path.GetDirectoryName(mapPath) ?? string.Empty;
        string[] candidates =
        {
            Path.Combine(mapFolder, WorkshopAssetFolderName),
            Path.Combine(mapFolder, "assetslocal"),
            GetLocalAssetsRoot()
        };

        string? existing = candidates.FirstOrDefault(Directory.Exists);
        if (existing is not null)
            txtAssets.Text = existing;
    }

    private string ResolveAssetsFolder(string sourcePath)
    {
        string selected = txtAssets.Text.Trim();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            if (!Directory.Exists(selected))
                throw new DirectoryNotFoundException("Assets folder does not exist: " + selected);

            return selected;
        }

        string mapFolder = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string[] candidates =
        {
            Path.Combine(mapFolder, WorkshopAssetFolderName),
            Path.Combine(mapFolder, "assetslocal"),
            GetLocalAssetsRoot()
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static void CopyAssetLocalFolder(string sourceAssetRoot, string targetAssetRoot, List<CopiedAsset> copied, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetRoot))
        {
            warnings.Add("No assetlocal/assetslocal folder selected or found. Empty assetlocal will be created.");
            return;
        }

        if (!Directory.Exists(sourceAssetRoot))
        {
            warnings.Add("Assets folder does not exist: " + sourceAssetRoot);
            return;
        }

        string sourceFull = AppendDirectorySeparator(Path.GetFullPath(sourceAssetRoot));
        string targetFull = AppendDirectorySeparator(Path.GetFullPath(targetAssetRoot));
        bool sameFolder = string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(sourceAssetRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceAssetRoot, file);
            if (!IsSafeRelativePath(relative))
            {
                warnings.Add("Skipped unsafe asset path: " + relative);
                continue;
            }

            string destination = Path.Combine(targetAssetRoot, relative);
            if (!sameFolder)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }

            copied.Add(new CopiedAsset(destination, Path.Combine(WorkshopAssetFolderName, relative)));
        }
    }

    private static void CopyReferencedAssets(JsonNode data, string targetAssetFolder, string mapFolder, string assetRootFolder, List<CopiedAsset> copied, List<string> warnings)
    {
        foreach (string background in CollectBackgroundFiles(data))
        {
            string safeName = SanitizeBackgroundName(background);
            string? source = FindBackgroundSource(safeName, mapFolder, assetRootFolder);
            if (source is null)
            {
                warnings.Add("Missing background PNG: " + safeName);
                continue;
            }

            string destination = Path.Combine(targetAssetFolder, safeName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
            copied.Add(new CopiedAsset(destination, Path.Combine(WorkshopAssetFolderName, BackgroundImagesFolderName, safeName)));
        }
    }

    private static HashSet<string> CollectBackgroundFiles(JsonNode data)
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        JsonObject obj = data.AsObject();
        AddBackgroundFile(files, obj["background"]);

        if (obj["backgrounds"] is JsonArray backgrounds)
        {
            foreach (JsonNode? background in backgrounds)
                AddBackgroundFile(files, background);
        }

        return files;
    }

    private static void AddBackgroundFile(HashSet<string> files, JsonNode? background)
    {
        if (background is not JsonObject bg)
            return;

        bool enabled = bg["enabled"]?.GetValue<bool>() ?? false;
        if (!enabled)
            return;

        string fileName = bg["fileName"]?.GetValue<string>() ?? "background.png";
        files.Add(SanitizeBackgroundName(fileName));
    }

    private static string? FindBackgroundSource(string fileName, string mapFolder, string assetRootFolder)
    {
        List<string> candidates = new();

        if (!string.IsNullOrWhiteSpace(assetRootFolder))
            candidates.Add(Path.Combine(assetRootFolder, BackgroundImagesFolderName, fileName));

        candidates.AddRange(new[]
        {
            Path.Combine(mapFolder, WorkshopAssetFolderName, BackgroundImagesFolderName, fileName),
            Path.Combine(mapFolder, "assetslocal", BackgroundImagesFolderName, fileName),
            Path.Combine(GetLocalAssetsRoot(), BackgroundImagesFolderName, fileName)
        });

        return candidates.FirstOrDefault(File.Exists);
    }

    private void TryInitializeSteam()
    {
        try
        {
            EnsureSteamAppIdFile();
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());

            if (!SteamAPI.Init())
            {
                lblSteam.Text = $"Steam App ID: {AppId} (Steam not initialized)";
                Log("Steam API failed to initialize. Open Steam before publishing.");
                return;
            }

            steamInitialized = true;
            lblSteam.Text = $"Steam App ID: {AppId} | User: {SteamFriends.GetPersonaName()}";
            Log("Steam initialized. User: " + SteamFriends.GetPersonaName());

            steamTimer = new System.Windows.Forms.Timer { Interval = 100 };
            steamTimer.Tick += (_, _) =>
            {
                try { SteamAPI.RunCallbacks(); }
                catch { }
            };
            steamTimer.Start();
        }
        catch (Exception ex)
        {
            lblSteam.Text = $"Steam App ID: {AppId} (Steam error)";
            Log("Steam init error: " + ex.Message);
        }
    }

    private static void EnsureSteamAppIdFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
        File.WriteAllText(path, AppId.ToString());
    }

    private void LoadMetadata()
    {
        try
        {
            currentMetaPath = GetMetaPath();
            if (!File.Exists(currentMetaPath))
            {
                Log("No metadata found for this map.");
                return;
            }

            JsmToolMeta? meta = JsonSerializer.Deserialize<JsmToolMeta>(File.ReadAllText(currentMetaPath));
            if (meta is null)
                return;

            txtTitle.Text = meta.Title ?? txtTitle.Text;
            txtDescription.Text = meta.Description ?? string.Empty;
            txtAssets.Text = meta.AssetFolder ?? txtAssets.Text;
            txtPreview.Text = meta.Preview ?? string.Empty;
            txtWorkshopId.Text = meta.WorkshopId?.ToString() ?? string.Empty;
            txtChangeNote.Text = meta.ChangeNote ?? txtChangeNote.Text;

            if (!string.IsNullOrWhiteSpace(meta.Visibility))
            {
                int index = cboVisibility.Items.IndexOf(meta.Visibility);
                if (index >= 0)
                    cboVisibility.SelectedIndex = index;
            }

            Log("Metadata loaded.");
        }
        catch (Exception ex)
        {
            Log("Metadata error: " + ex.Message);
        }
    }

    private void SaveMetadata(string? workshopId)
    {
        try
        {
            currentMetaPath = GetMetaPath();
            Directory.CreateDirectory(Path.GetDirectoryName(currentMetaPath)!);
            JsmToolMeta meta = new()
            {
                Title = txtTitle.Text.Trim(),
                Description = txtDescription.Text.Trim(),
                AssetFolder = txtAssets.Text.Trim(),
                Preview = txtPreview.Text.Trim(),
                ChangeNote = txtChangeNote.Text.Trim(),
                Visibility = cboVisibility.SelectedItem?.ToString(),
                WorkshopId = ulong.TryParse(workshopId, out ulong parsed) ? parsed : null
            };

            File.WriteAllText(currentMetaPath, JsonSerializer.Serialize(meta, JsonOptions));
        }
        catch (Exception ex)
        {
            Log("Could not save metadata: " + ex.Message);
        }
    }

    private string GetMetaPath()
    {
        string sourcePath = txtMap.Text.Trim();
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Select a map first.");

        string folder = Path.GetDirectoryName(sourcePath) ?? GetCreationsFolder();
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(folder, name + ".jsmmeta.json");
    }

    private ERemoteStoragePublishedFileVisibility GetSelectedVisibility()
    {
        return cboVisibility.SelectedIndex switch
        {
            0 => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
            1 => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
            3 => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
            _ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate
        };
    }

    private void StartUploadProgressTimer()
    {
        progressTimer?.Stop();
        progressTimer = new System.Windows.Forms.Timer { Interval = 250 };
        progressTimer.Tick += (_, _) =>
        {
            try
            {
                EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(currentUpdate, out ulong done, out ulong total);
                if (total == 0)
                {
                    progress.Style = ProgressBarStyle.Marquee;
                    lblProgress.Text = "Progress: waiting...";
                    return;
                }

                int percent = (int)Math.Clamp(done * 100 / total, 0, 100);
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = percent;
                lblProgress.Text = $"Progress: {percent}% - {status}";
            }
            catch
            {
                progress.Style = ProgressBarStyle.Marquee;
            }
        };
        progressTimer.Start();
    }

    private void OpenLastFolder()
    {
        string folder = !string.IsNullOrWhiteSpace(lastJsmPath)
            ? Path.GetDirectoryName(lastJsmPath) ?? GetWorkshopRoot()
            : GetWorkshopRoot();

        Directory.CreateDirectory(folder);
        Process.Start("explorer.exe", folder);
    }

    private void ResetProgress(string text)
    {
        progress.Style = ProgressBarStyle.Continuous;
        progress.Value = 0;
        lblProgress.Text = text;
    }

    private void CompleteProgress()
    {
        progress.Style = ProgressBarStyle.Continuous;
        progress.Value = 100;
        lblProgress.Text = "Completed";
    }

    private void FailProgress()
    {
        progress.Style = ProgressBarStyle.Continuous;
        progress.Value = 0;
        lblProgress.Text = "Error";
    }

    private void Log(string message)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static string SafeWorkshopText(string value, string fallback)
    {
        string clean = value.Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static string SanitizeMapName(string name)
    {
        string safe = string.IsNullOrWhiteSpace(name) ? "map1" : name.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(safe) ? "map1" : safe;
    }

    private static string SanitizeWorkshopId(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? LocalTestWorkshopId : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(safe) ? LocalTestWorkshopId : safe;
    }

    private static string SanitizeBackgroundName(string fileName)
    {
        string safe = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "background.png" : fileName.Trim());
        foreach (char invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return Path.GetExtension(safe).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? safe
            : Path.GetFileNameWithoutExtension(safe) + ".png";
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        if (Path.IsPathRooted(relativePath))
            return false;

        string normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.Equals("..", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    private static string GetDocumentsRoot()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(string.IsNullOrWhiteSpace(docs) ? AppContext.BaseDirectory : docs, "jumpfall");
    }

    private static string GetLevelsRoot() => Path.Combine(GetDocumentsRoot(), "levels");
    private static string GetCreationsFolder() => Path.Combine(GetLevelsRoot(), "creations");
    private static string GetCompilatorFolder() => Path.Combine(GetLevelsRoot(), "compilator");
    private static string GetWorkshopRoot() => Path.Combine(GetLevelsRoot(), "workshop");
    private static string GetLocalAssetsRoot() => Path.Combine(GetLevelsRoot(), "assetslocal");

    private sealed record CopiedAsset(string FullPath, string ZipPath);
    private sealed record JsmBuildResult(string JsmPath, string JfuePath, string WorkshopId, List<string> Warnings);

    private sealed class JsmManifest
    {
        [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = PackageFormatVersion;
        [JsonPropertyName("packageType")] public string PackageType { get; set; } = MainForm.PackageType;
        [JsonPropertyName("mapFile")] public string MapFile { get; set; } = string.Empty;
        [JsonPropertyName("workshopId")] public string WorkshopId { get; set; } = string.Empty;
        [JsonPropertyName("createdUtc")] public string CreatedUtc { get; set; } = string.Empty;
    }

    private sealed class JsmToolMeta
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("asset_folder")] public string? AssetFolder { get; set; }
        [JsonPropertyName("preview")] public string? Preview { get; set; }
        [JsonPropertyName("visibility")] public string? Visibility { get; set; }
        [JsonPropertyName("change_note")] public string? ChangeNote { get; set; }
        [JsonPropertyName("workshop_id")] public ulong? WorkshopId { get; set; }
    }
}
