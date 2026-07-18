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
    private const int CurrentLevelDataVersion = 19;
    private const string LocalTestWorkshopId = "local_test";
    private const string WorkshopAssetFolderName = "assetlocal";
    private const string BackgroundImagesFolderName = "backgroundimg";
    private const string SoundFolderName = "sound";
    private const string LuaFolderName = "lua";
    private const string WorkshopPreviewFileName = "preview.png";

    private const long MaxPackageBytes = 256L * 1024L * 1024L;
    private const long MaxExtractedBytes = 512L * 1024L * 1024L;
    private const long MaxSingleEntryBytes = 128L * 1024L * 1024L;
    private const long MaxImageFileBytes = 32L * 1024L * 1024L;
    private const long MaxVideoFileBytes = 128L * 1024L * 1024L;
    private const long MaxLuaScriptBytes = 512L * 1024L;
    private const int MaxArchiveFiles = 1500;
    private const int MaxTextureDimension = 4096;
    private const long MaxTexturePixels = 4096L * 4096L;
    private const int MaxBossesPerMap = 8;
    private const int MaxBossNodesPerEncounter = 128;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> KnownPieceIds = new(StringComparer.Ordinal)
    {
        "box_ground",
        "checkpoint",
        "apple",
        "orb_jump",
        "plane_jump",
        "elevator"
    };

    private static readonly HashSet<string> KnownTriggerIds = new(StringComparer.Ordinal)
    {
        "limit_map",
        "deathzone",
        "changelevel",
        "finish_level",
        "lua_event",
        "static_camera",
        "visibility",
        "event",
        "timer",
        "wall_jump",
        "triggered_ground"
    };

    private static readonly HashSet<string> SupportedBackgroundExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".mp4",
        ".webm"
    };

    private static readonly HashSet<string> SupportedSoundExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".ogg"
    };

    private static readonly HashSet<string> SupportedPackageAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".png",
        ".mp4",
        ".webm",
        ".wav",
        ".ogg",
        ".lua",
        ".txt",
        ".md"
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
        root.Controls.Add(new Label { Text = "Compiled map (.jfue):", AutoSize = true }, 0, 1);
        txtMap.Dock = DockStyle.Fill;
        root.SetColumnSpan(txtMap, 4);
        root.Controls.Add(txtMap, 1, 1);

        var browseMap = new Button { Text = "Examine..." };
        browseMap.Click += BrowseMap;
        root.Controls.Add(browseMap, 5, 1);

        root.Controls.Add(new Label { Text = "Assets folder:", AutoSize = true }, 0, 2);
        txtAssets.Dock = DockStyle.Fill;
        txtAssets.PlaceholderText = @"Auto: Documents\jumpfall\levels\assetlocal";
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
            Filter = "Jumpfall compiled maps|*.jfue|All files|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(GetCompilatorFolder()) ? GetCompilatorFolder() : GetLevelsRoot()
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
            Description = "Select the assetlocal folder for this map. Legacy assetslocal folders are still supported.",
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
            Filter = "PNG images|*.png|All files|*.*",
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
            throw new InvalidOperationException("Select a valid compiled .jfue file.");

        if (!Path.GetExtension(sourcePath).Equals(".jfue", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workshop packages must be built from .jfue only. Do not publish .jmap editable files.");

        string safeWorkshopId = SanitizeWorkshopId(workshopId);
        string mapName = SanitizeMapName(Path.GetFileNameWithoutExtension(sourcePath));
        string jfuePath = sourcePath;
        JsonNode mapData = ReadJsonNode(jfuePath);
        List<string> warnings = ValidateLevelData(mapData);
        string assetRootFolder = ResolveAssetsFolder(sourcePath);

        string workshopFolder = Path.Combine(GetWorkshopRoot(), safeWorkshopId);
        string workshopAssetFolder = Path.Combine(workshopFolder, WorkshopAssetFolderName);
        string backgroundAssetFolder = Path.Combine(workshopAssetFolder, BackgroundImagesFolderName);
        string soundAssetFolder = Path.Combine(workshopAssetFolder, SoundFolderName);
        string luaAssetFolder = Path.Combine(workshopAssetFolder, LuaFolderName);
        Directory.CreateDirectory(workshopFolder);
        Directory.CreateDirectory(workshopAssetFolder);
        Directory.CreateDirectory(backgroundAssetFolder);
        Directory.CreateDirectory(soundAssetFolder);
        Directory.CreateDirectory(luaAssetFolder);

        string mapFileName = Path.GetFileName(jfuePath);
        string targetMapPath = Path.Combine(workshopFolder, mapFileName);
        CopyFileIfDifferent(jfuePath, targetMapPath);

        string? targetPreviewPath = CopyPreviewToWorkshopFolder(workshopFolder, warnings);

        List<CopiedAsset> copiedAssets = new();
        CopyAssetLocalFolder(assetRootFolder, workshopAssetFolder, copiedAssets, warnings);
        CopyReferencedAssets(mapData, backgroundAssetFolder, Path.GetDirectoryName(jfuePath) ?? string.Empty, assetRootFolder, copiedAssets, warnings);
        CopyReferencedSoundtrackAssets(mapData, soundAssetFolder, Path.GetDirectoryName(jfuePath) ?? string.Empty, assetRootFolder, copiedAssets, warnings);
        CopyReferencedLuaAssets(mapData, luaAssetFolder, Path.GetDirectoryName(jfuePath) ?? string.Empty, assetRootFolder, copiedAssets, warnings);

        List<CopiedAsset> uniqueAssets = copiedAssets
            .DistinctBy(asset => asset.ZipPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ValidatePackageContents(targetMapPath, targetPreviewPath, uniqueAssets);

        string jsmPath = Path.Combine(workshopFolder, Path.GetFileNameWithoutExtension(mapFileName) + ".jsm");
        string mapTitle = SafeWorkshopText(txtTitle.Text, mapName);
        string authorName = GetAuthorName();
        JsmManifest manifest = new()
        {
            MapFile = mapFileName,
            WorkshopId = safeWorkshopId,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            MapName = mapTitle,
            Title = mapTitle,
            Author = authorName,
            AuthorName = authorName,
            Preview = !string.IsNullOrWhiteSpace(targetPreviewPath) ? WorkshopPreviewFileName : string.Empty
        };

        string temporaryJsmPath = jsmPath + ".tmp";
        if (File.Exists(temporaryJsmPath))
            File.Delete(temporaryJsmPath);

        try
        {
            using (FileStream stream = new(temporaryJsmPath, FileMode.CreateNew, FileAccess.ReadWrite))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create, false))
            {
                zip.CreateEntryFromFile(targetMapPath, mapFileName, CompressionLevel.Optimal);

                if (!string.IsNullOrWhiteSpace(targetPreviewPath) && File.Exists(targetPreviewPath))
                    zip.CreateEntryFromFile(targetPreviewPath, WorkshopPreviewFileName, CompressionLevel.Optimal);

                zip.CreateEntry(WorkshopAssetFolderName + "/");
                zip.CreateEntry(WorkshopAssetFolderName + "/" + BackgroundImagesFolderName + "/");
                zip.CreateEntry(WorkshopAssetFolderName + "/" + SoundFolderName + "/");
                zip.CreateEntry(WorkshopAssetFolderName + "/" + LuaFolderName + "/");

                foreach (CopiedAsset asset in uniqueAssets)
                    zip.CreateEntryFromFile(asset.FullPath, asset.ZipPath.Replace('\\', '/'), CompressionLevel.Optimal);

                ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using StreamWriter writer = new(manifestEntry.Open());
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
            }

            long packageBytes = new FileInfo(temporaryJsmPath).Length;
            if (packageBytes > MaxPackageBytes)
                throw new InvalidDataException($"The .jsm exceeds Jumpfall's {FormatMiB(MaxPackageBytes)} MiB package limit.");

            File.Move(temporaryJsmPath, jsmPath, true);
        }
        catch
        {
            if (File.Exists(temporaryJsmPath))
                File.Delete(temporaryJsmPath);
            throw;
        }

        return new JsmBuildResult(jsmPath, targetMapPath, safeWorkshopId, warnings);
    }

    private string? CopyPreviewToWorkshopFolder(string workshopFolder, List<string> warnings)
    {
        string previewPath = txtPreview.Text.Trim();
        string targetPreviewPath = Path.Combine(workshopFolder, WorkshopPreviewFileName);

        if (string.IsNullOrWhiteSpace(previewPath))
        {
            if (File.Exists(targetPreviewPath))
                File.Delete(targetPreviewPath);

            warnings.Add("No preview PNG selected. The .jsm will not include preview.png.");
            return null;
        }

        if (!File.Exists(previewPath))
            throw new FileNotFoundException("Preview file does not exist.", previewPath);

        if (!Path.GetExtension(previewPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Preview must be a .png file. It will be stored as preview.png.");

        ValidateAssetFile(previewPath, "preview image");
        ValidatePngDimensions(previewPath, "preview image");

        string sourceFull = Path.GetFullPath(previewPath);
        string targetFull = Path.GetFullPath(targetPreviewPath);

        if (!string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase))
            File.Copy(sourceFull, targetFull, true);

        return targetPreviewPath;
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

        if (obj["version"] is JsonValue versionValue && versionValue.TryGetValue(out int version))
        {
            if (version > CurrentLevelDataVersion)
                throw new InvalidDataException($"This map uses LevelData v{version}, but this compiler supports up to v{CurrentLevelDataVersion}.");

            if (version < CurrentLevelDataVersion)
                warnings.Add($"Map uses LevelData v{version}. Jumpfall will normalize it to v{CurrentLevelDataVersion} when loaded.");
        }
        else
        {
            warnings.Add("Map has no valid LevelData version. Jumpfall will treat it as a legacy map.");
        }

        if (obj["spawnPoint"] is null)
            warnings.Add("Map has no spawnPoint.");

        ValidateBackgroundNode(obj["background"], "background", warnings);
        if (obj["backgrounds"] is JsonArray backgrounds)
        {
            for (int i = 0; i < backgrounds.Count; i++)
                ValidateBackgroundNode(backgrounds[i], $"backgrounds[{i}]", warnings);
        }

        if (CollectBackgroundFiles(data).Any(IsVideoBackgroundFile))
        {
            warnings.Add("This map uses a video background. The current Linux build will reject it as 'Map not compatible'; Windows and macOS remain available.");
        }

        ValidateSoundtrack(obj["soundtrack"], warnings);
        ValidateLua(obj["lua"], warnings);

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

        if (obj["bosses"] is JsonArray bosses)
        {
            if (bosses.Count > MaxBossesPerMap)
                throw new InvalidDataException($"Map has {bosses.Count} bosses; Jumpfall allows at most {MaxBossesPerMap}.");

            for (int i = 0; i < bosses.Count; i++)
            {
                if (bosses[i]?["nodes"] is JsonArray nodes && nodes.Count > MaxBossNodesPerEncounter)
                    throw new InvalidDataException($"Boss {i} has {nodes.Count} nodes; Jumpfall allows at most {MaxBossNodesPerEncounter}.");
            }
        }

        return warnings;
    }

    private static void ValidateBackgroundNode(JsonNode? node, string label, List<string> warnings)
    {
        if (node is not JsonObject background || !(background["enabled"]?.GetValue<bool>() ?? false))
            return;

        string fileName = background["fileName"]?.GetValue<string>() ?? "background.png";
        ValidateLocalFileName(fileName, label);

        string extension = Path.GetExtension(fileName);
        if (!SupportedBackgroundExtensions.Contains(extension))
            throw new InvalidDataException($"{label} must reference a .png, .mp4, or .webm file.");

        if (IsVideoBackgroundFile(fileName) && background["loopVideo"] is null)
            warnings.Add(label + " has no loopVideo value. Jumpfall will apply its version-compatible default.");
    }

    private static void ValidateSoundtrack(JsonNode? node, List<string> warnings)
    {
        if (node is not JsonObject soundtrack || !(soundtrack["enabled"]?.GetValue<bool>() ?? false))
            return;

        if (soundtrack["tracks"] is not JsonArray tracks)
        {
            warnings.Add("Soundtrack is enabled but has no tracks array.");
            return;
        }

        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i] is not JsonObject track)
                continue;

            string fileName = track["fileName"]?.GetValue<string>() ?? string.Empty;
            ValidateLocalFileName(fileName, $"soundtrack track {i}");
            if (!SupportedSoundExtensions.Contains(Path.GetExtension(fileName)))
                throw new InvalidDataException($"Soundtrack track {i} must reference a .wav or .ogg file.");

            if (track["volume"] is JsonValue volumeValue &&
                volumeValue.TryGetValue(out double volume) &&
                (volume < 0d || volume > 1d))
            {
                warnings.Add($"Soundtrack track {i} volume is outside 0..1 and will be clamped by Jumpfall.");
            }
        }
    }

    private static void ValidateLua(JsonNode? node, List<string> warnings)
    {
        if (node is not JsonObject lua || !(lua["enabled"]?.GetValue<bool>() ?? false))
            return;

        string entryFile = lua["entryFile"]?.GetValue<string>() ?? "main.lua";
        ValidateLocalFileName(entryFile, "Lua entryFile");
        if (!Path.GetExtension(entryFile).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Lua entryFile must reference a .lua file.");

        if (lua["allowInWorkshop"] is JsonValue allowValue &&
            allowValue.TryGetValue(out bool allowInWorkshop) &&
            !allowInWorkshop)
        {
            warnings.Add("Lua is enabled but allowInWorkshop is false; Workshop runtime will not execute it.");
        }
    }

    private static void ValidateLocalFileName(string fileName, string label)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException(label + " has an empty file name.");

        string trimmed = fileName.Trim();
        if (Path.IsPathRooted(trimmed) || !string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal))
            throw new InvalidDataException(label + " must use a local file name without folders.");
    }

    private static bool IsVideoBackgroundFile(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webm", StringComparison.OrdinalIgnoreCase);
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
            GetLocalAssetsRoot(),
            GetLegacyLocalAssetsRoot()
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
            GetLocalAssetsRoot(),
            GetLegacyLocalAssetsRoot()
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static void CopyAssetLocalFolder(string sourceAssetRoot, string targetAssetRoot, List<CopiedAsset> copied, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetRoot))
        {
            warnings.Add("No assetlocal folder was selected or found. An empty assetlocal structure will be created.");
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

            if (!IsSupportedAssetPath(relative, out string reason))
            {
                warnings.Add("Skipped asset: " + relative + " (" + reason + ")");
                continue;
            }

            ValidateAssetFile(file, relative);
            if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
                ValidatePngDimensions(file, relative);

            string destination = Path.Combine(targetAssetRoot, relative);
            if (!sameFolder)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                CopyFileIfDifferent(file, destination);
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
                warnings.Add("Missing background asset: " + safeName);
                continue;
            }

            ValidateAssetFile(source, "background " + safeName);
            if (Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
                ValidatePngDimensions(source, "background " + safeName);

            string destination = Path.Combine(targetAssetFolder, safeName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyFileIfDifferent(source, destination);
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
        return FindReferencedAssetSource(BackgroundImagesFolderName, fileName, mapFolder, assetRootFolder);
    }

    private static void CopyReferencedSoundtrackAssets(JsonNode data, string targetAssetFolder, string mapFolder, string assetRootFolder, List<CopiedAsset> copied, List<string> warnings)
    {
        JsonObject obj = data.AsObject();
        if (obj["soundtrack"] is not JsonObject soundtrack || !(soundtrack["enabled"]?.GetValue<bool>() ?? false) || soundtrack["tracks"] is not JsonArray tracks)
            return;

        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? node in tracks)
        {
            if (node is JsonObject track)
            {
                string fileName = track["fileName"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fileName))
                    fileNames.Add(Path.GetFileName(fileName.Trim()));
            }
        }

        CopyReferencedFolderAssets(fileNames, SoundFolderName, "soundtrack", targetAssetFolder, mapFolder, assetRootFolder, copied, warnings);
    }

    private static void CopyReferencedLuaAssets(JsonNode data, string targetAssetFolder, string mapFolder, string assetRootFolder, List<CopiedAsset> copied, List<string> warnings)
    {
        JsonObject obj = data.AsObject();
        if (obj["lua"] is not JsonObject lua || !(lua["enabled"]?.GetValue<bool>() ?? false))
            return;

        string entryFile = lua["entryFile"]?.GetValue<string>() ?? "main.lua";
        CopyReferencedFolderAssets(
            new[] { Path.GetFileName(entryFile.Trim()) },
            LuaFolderName,
            "Lua",
            targetAssetFolder,
            mapFolder,
            assetRootFolder,
            copied,
            warnings);
    }

    private static void CopyReferencedFolderAssets(IEnumerable<string> fileNames, string folderName, string label, string targetAssetFolder, string mapFolder, string assetRootFolder, List<CopiedAsset> copied, List<string> warnings)
    {
        foreach (string fileName in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? source = FindReferencedAssetSource(folderName, fileName, mapFolder, assetRootFolder);
            if (source is null)
            {
                warnings.Add($"Missing {label} asset: {fileName}");
                continue;
            }

            ValidateAssetFile(source, label + " " + fileName);
            string destination = Path.Combine(targetAssetFolder, fileName);
            CopyFileIfDifferent(source, destination);
            copied.Add(new CopiedAsset(destination, Path.Combine(WorkshopAssetFolderName, folderName, fileName)));
        }
    }

    private static string? FindReferencedAssetSource(string folderName, string fileName, string mapFolder, string assetRootFolder)
    {
        List<string> candidates = new();

        if (!string.IsNullOrWhiteSpace(assetRootFolder))
            candidates.Add(Path.Combine(assetRootFolder, folderName, fileName));

        candidates.AddRange(new[]
        {
            Path.Combine(mapFolder, WorkshopAssetFolderName, folderName, fileName),
            Path.Combine(mapFolder, "assetslocal", folderName, fileName),
            Path.Combine(GetLocalAssetsRoot(), folderName, fileName),
            Path.Combine(GetLegacyLocalAssetsRoot(), folderName, fileName)
        });

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsSupportedAssetPath(string relativePath, out string reason)
    {
        string extension = Path.GetExtension(relativePath);
        if (SupportedPackageAssetExtensions.Contains(extension))
        {
            reason = string.Empty;
            return true;
        }

        reason = string.IsNullOrWhiteSpace(extension)
            ? "files without an extension are not accepted by Jumpfall"
            : "extension " + extension + " is not accepted by Jumpfall";
        return false;
    }

    private static void ValidatePackageContents(string mapPath, string? previewPath, IReadOnlyCollection<CopiedAsset> assets)
    {
        ValidateAssetFile(mapPath, "compiled map");

        int fileCount = 2 + assets.Count;
        long extractedBytes = new FileInfo(mapPath).Length;

        if (!string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath))
        {
            ValidateAssetFile(previewPath, "preview image");
            fileCount++;
            extractedBytes += new FileInfo(previewPath).Length;
        }

        foreach (CopiedAsset asset in assets)
        {
            if (!File.Exists(asset.FullPath))
                throw new FileNotFoundException("Asset disappeared before packaging.", asset.FullPath);

            ValidateAssetFile(asset.FullPath, asset.ZipPath);
            extractedBytes += new FileInfo(asset.FullPath).Length;
        }

        if (fileCount > MaxArchiveFiles)
            throw new InvalidDataException($"The package contains {fileCount} files; Jumpfall allows at most {MaxArchiveFiles}.");

        if (extractedBytes > MaxExtractedBytes)
            throw new InvalidDataException($"The package expands to more than {FormatMiB(MaxExtractedBytes)} MiB.");
    }

    private static void ValidateAssetFile(string path, string label)
    {
        FileInfo info = new(path);
        if (!info.Exists)
            throw new FileNotFoundException(label + " does not exist.", path);

        if (info.Length > MaxSingleEntryBytes)
            throw new InvalidDataException($"{label} exceeds Jumpfall's {FormatMiB(MaxSingleEntryBytes)} MiB per-file limit.");

        string extension = info.Extension;
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && info.Length > MaxImageFileBytes)
            throw new InvalidDataException($"{label} exceeds Jumpfall's {FormatMiB(MaxImageFileBytes)} MiB image limit.");

        if (IsVideoBackgroundFile(info.Name) && info.Length > MaxVideoFileBytes)
            throw new InvalidDataException($"{label} exceeds Jumpfall's {FormatMiB(MaxVideoFileBytes)} MiB video limit.");

        if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) && info.Length > MaxLuaScriptBytes)
            throw new InvalidDataException($"{label} exceeds Jumpfall's {FormatMiB(MaxLuaScriptBytes):0.0} MiB Lua limit.");
    }

    private static void ValidatePngDimensions(string path, string label)
    {
        try
        {
            using Image image = Image.FromFile(path);
            if (image.RawFormat.Guid != System.Drawing.Imaging.ImageFormat.Png.Guid)
                throw new InvalidDataException(label + " has a .png extension but is not a valid PNG image.");

            long pixels = (long)image.Width * image.Height;
            if (image.Width > MaxTextureDimension || image.Height > MaxTextureDimension || pixels > MaxTexturePixels)
            {
                throw new InvalidDataException(
                    $"{label} is {image.Width}x{image.Height}; Jumpfall allows at most {MaxTextureDimension}x{MaxTextureDimension}.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(label + " could not be decoded as PNG: " + exception.Message, exception);
        }
    }

    private static void CopyFileIfDifferent(string sourcePath, string destinationPath)
    {
        string sourceFull = Path.GetFullPath(sourcePath);
        string destinationFull = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFull, destinationFull, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFull)!);
        File.Copy(sourceFull, destinationFull, true);
    }

    private static double FormatMiB(long bytes)
    {
        return bytes / (1024d * 1024d);
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

    private string GetAuthorName()
    {
        if (steamInitialized)
        {
            try
            {
                string steamName = SteamFriends.GetPersonaName();
                if (!string.IsNullOrWhiteSpace(steamName))
                    return steamName.Trim();
            }
            catch
            {
                // Keep packaging usable offline; the local OS user is a safe fallback for metadata.
            }
        }

        return string.IsNullOrWhiteSpace(Environment.UserName) ? "Unknown" : Environment.UserName;
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

        string extension = Path.GetExtension(safe);
        if (!SupportedBackgroundExtensions.Contains(extension))
            extension = ".png";

        string name = Path.GetFileNameWithoutExtension(safe);
        if (string.IsNullOrWhiteSpace(name))
            name = "background";

        return name + extension.ToLowerInvariant();
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
    private static string GetLocalAssetsRoot() => Path.Combine(GetLevelsRoot(), "assetlocal");
    private static string GetLegacyLocalAssetsRoot() => Path.Combine(GetLevelsRoot(), "assetslocal");

    private sealed record CopiedAsset(string FullPath, string ZipPath);
    private sealed record JsmBuildResult(string JsmPath, string JfuePath, string WorkshopId, List<string> Warnings);

    private sealed class JsmManifest
    {
        [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = PackageFormatVersion;
        [JsonPropertyName("packageType")] public string PackageType { get; set; } = MainForm.PackageType;
        [JsonPropertyName("mapFile")] public string MapFile { get; set; } = string.Empty;
        [JsonPropertyName("workshopId")] public string WorkshopId { get; set; } = string.Empty;
        [JsonPropertyName("mapName")] public string MapName { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;
        [JsonPropertyName("authorName")] public string AuthorName { get; set; } = string.Empty;
        [JsonPropertyName("preview")] public string Preview { get; set; } = string.Empty;
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
