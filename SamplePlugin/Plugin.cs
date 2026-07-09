using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SamplePlugin.Services;
using SamplePlugin.Windows;
using System.Threading.Tasks;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/aic";

    public Configuration Configuration { get; init; }
    public ActivityLog ActivityLog { get; init; }
    public EmoteRegistry EmoteRegistry { get; init; }
    public AetheryteResolver AetheryteResolver { get; init; }
    public InventoryService InventoryService { get; init; }
    public NavmeshIpc NavmeshIpc { get; init; }
    public TradeMonitor TradeMonitor { get; init; }
    public PresetTradeService PresetTradeService { get; init; }
    public CharacterProfileService CharacterProfileService { get; init; }
    public UiAssetService UiAssets { get; init; }
    public RuntimeControls RuntimeControls { get; init; }
    public PlayerMemory PlayerMemory { get; init; }
    public OutboundEchoGuard EchoGuard { get; init; }
    public InteractionSafetyService SafetyService { get; init; }

    public readonly WindowSystem WindowSystem = new("AICompanion");

    private readonly AiService aiService;
    private readonly ChatMonitor chatMonitor;
    private readonly TradeMonitor tradeMonitor;
    private readonly InteractionOrchestrator orchestrator;
    private readonly ConfigWindow configWindow;
    private readonly MainWindow mainWindow;

    public string OrchestratorLastDecision => orchestrator.LastDecisionSummary;

    public Task<bool> TestAiConnectionAsync() => orchestrator.TestAiConnectionAsync();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ActivityLog = new ActivityLog();
        RuntimeControls = new RuntimeControls();
        PlayerMemory = new PlayerMemory();
        EchoGuard = new OutboundEchoGuard();
        SafetyService = new InteractionSafetyService(Configuration, ObjectTable, PartyList, ActivityLog);
        EmoteRegistry = new EmoteRegistry(DataManager, ActivityLog);
        AetheryteResolver = new AetheryteResolver(DataManager, ActivityLog);
        var travelIntentService = new TravelIntentService(AetheryteResolver);
        InventoryService = new InventoryService(DataManager, ActivityLog);
        PresetTradeService = new PresetTradeService(Configuration, InventoryService);
        var characterProfileService = new CharacterProfileService(DataManager, PlayerState, UnlockState, ActivityLog);
        CharacterProfileService = characterProfileService;
        UiAssets = new UiAssetService(TextureProvider, PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty);
        UiAssets.EnsureLoaded();
        NavmeshIpc navmeshIpc = new(PluginInterface, ActivityLog);
        NavmeshIpc = navmeshIpc;
        tradeMonitor = new TradeMonitor(Framework, GameGui, ObjectTable, Configuration, RuntimeControls, ActivityLog, EchoGuard, PresetTradeService);
        TradeMonitor = tradeMonitor;
        var playerTargeting = new PlayerTargeting(ObjectTable, TargetManager);
        var partyInviteHelper = new PartyInviteHelper(playerTargeting, DataManager, ActivityLog);

        var socialActionService = new SocialActionService(
            Configuration,
            RuntimeControls,
            AetheryteResolver,
            InventoryService,
            tradeMonitor,
            navmeshIpc,
            PresetTradeService,
            characterProfileService,
            ObjectTable,
            ClientState,
            playerTargeting,
            partyInviteHelper,
            ActivityLog);

        var adminHandler = new AdminCommandHandler(Configuration, RuntimeControls, socialActionService, navmeshIpc, ActivityLog);
        aiService = new AiService(
            Configuration,
            EmoteRegistry,
            AetheryteResolver,
            navmeshIpc,
            PresetTradeService,
            characterProfileService,
            ActivityLog);
        var tradeIntentConfirmationService = new TradeIntentConfirmationService(
            Configuration, PresetTradeService, InventoryService, EchoGuard, ActivityLog);
        orchestrator = new InteractionOrchestrator(
            Configuration,
            aiService,
            EmoteRegistry,
            RuntimeControls,
            EchoGuard,
            socialActionService,
            tradeIntentConfirmationService,
            SafetyService,
            Framework,
            ActivityLog);
        chatMonitor = new ChatMonitor(
            ChatGui,
            PlayerState,
            Configuration,
            ActivityLog,
            adminHandler,
            PlayerMemory,
            EchoGuard,
            InventoryService,
            PresetTradeService,
            travelIntentService,
            SafetyService);

        chatMonitor.OnEvent += orchestrator.Enqueue;

        configWindow = new ConfigWindow(this);
        mainWindow = new MainWindow(this);

        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 AI 互动插件主窗口",
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        Framework.RunOnFrameworkThread(() =>
        {
            EmoteRegistry.Refresh();
            AetheryteResolver.Refresh();
            characterProfileService.RefreshGearsets();
            InventoryService.EnsureInitialized();
        });

        ActivityLog.Info("AI 互动插件已加载。");
        Log.Information($"{PluginInterface.Manifest.Name} 已加载。");
    }

    public void Dispose()
    {
        chatMonitor.OnEvent -= orchestrator.Enqueue;
        chatMonitor.Dispose();
        tradeMonitor.Dispose();
        orchestrator.Dispose();
        aiService.Dispose();

        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();
        mainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => configWindow.Toggle();
    public void ToggleMainUI() => mainWindow.Toggle();
}
