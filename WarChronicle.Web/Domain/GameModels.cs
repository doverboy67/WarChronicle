namespace WarChronicle.Web.Domain;

public enum PlayAreaKind
{
    Clearing,
    Terrain
}

public enum TerrainType
{
    None,
    Plains,
    Forest,
    Mountain
}

public enum GamePhase
{
    Mobilization,
    Camp,
    EndOfSeason,
    Winter,
    Finale,
    GameOver
}

public enum MobilizationStep
{
    ExploreReady,
    TerrainChoice,
    ExploreEncounter,
    ArrivalReady,
    ArrivalEncounter,
    ArrivalComplete
}

public enum CampStep
{
    Ready,
    ResolvingSpecial,
    ChoosingAction,
    ResolvingAction,
    Complete
}

public enum EndOfSeasonStep
{
    Ready,
    VulnerabilityReady,
    HarvestReady,
    HarvestPreparing,
    HarvestPacking,
    ConsolidationReady,
    RefitReform,
    Complete
}

public enum WinterStep
{
    Ready,
    Attrition,
    RefitReform,
    Defeat,
    Complete
}

public sealed record PlayAreaElement(
    string Id,
    int Sequence,
    PlayAreaKind Kind,
    TerrainType Terrain = TerrainType.None,
    bool HasBarbarianIcon = false,
    string? Controller = null,
    bool IsHostile = false,
    bool IsHostLocation = false,
    bool IsDepleted = false,
    int BonusFood = 0);

public sealed record HostUnit(string Id, string Type, int Count);
public sealed record ResourceState(string Name, int Value);
public sealed record MarketState(string Resource, int BuyPrice, int SellPrice);
public sealed record AdvancementOfferState(string Id, string Name, string Cost, string Effect);
public sealed record AvailableForceState(string Type, int Available);
public sealed record PersistentState(string Name, string? Detail = null);
public sealed record BaggageSlot(int Position, string? Contents, int Quantity = 1, bool Damaged = false);
public sealed record TribeState(string Name, string Rapport);
public sealed record ExploreCardState(
    string Id,
    int CardNum,
    string Title,
    IReadOnlyList<string> TerrainKeywords,
    string Flavor,
    string Effect);

public sealed record ArrivalCardState(
    string Id,
    int CardNum,
    string Title,
    string Tribe,
    string Tribute,
    string Setup,
    string Effect,
    string Host);

public sealed record CampCardState(
    string Id,
    string CardType,
    string Set,
    int? CardNum,
    string? EchoPool,
    int? EchoNum,
    string Title,
    string? Keyword,
    string Location,
    int Time,
    string? CostType,
    string? CostValue,
    string AfterPlay,
    string Flavor,
    string Effect);

public enum FlowPromptKind
{
    Choice,
    Select,
    Test,
    Combat
}

public sealed record FlowPromptOption(string Id, string Label, string? Detail = null, bool Disabled = false);
public sealed record ChronicleChoice(string Id, string Label, string? Detail = null, bool Disabled = false);

public sealed record FlowPromptState(
    FlowPromptKind Kind,
    string Heading,
    string Prompt,
    IReadOnlyList<FlowPromptOption> Options);

public enum ChronicleTone
{
    Narrative,
    Result,
    Warning,
    System
}

public sealed record ChronicleEntry(
    string Calendar,
    string Text,
    ChronicleTone Tone = ChronicleTone.Narrative,
    string? Heading = null,
    IReadOnlyList<ChronicleChoice>? Choices = null,
    string? SelectedChoiceId = null);


public enum FinaleStep
{
    ResolveReady,
    ResolveChoose,
    DeployHost,
    RevealMiddelalderen,
    Combat,
    Complete
}

public sealed class FinaleState
{
    public bool IsTest { get; set; }
    public int? TestSeed { get; set; }
    public FinaleStep Step { get; set; } = FinaleStep.ResolveReady;
    public int Morale { get; set; }
    public int Leadership { get; set; }
    public bool HasPriest { get; set; }
    public List<int> ResolveRolls { get; } = [];
    public int? ResolveSelected { get; set; }
    public Dictionary<string, int> PlayerUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PlayerAdvancements { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CommittedTactics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SteadyAdvanceType { get; set; }
    public HashSet<string> MiddelalderenAdvancements { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> MiddelalderenUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Outcome { get; set; }
    public List<string> Log { get; } = [];
}

public enum CombatStep
{
    Setup,
    Priest,
    RoundStart,
    Archers,
    Cavalry,
    Infantry,
    Levy,
    CasualtyChoice,
    RerollChoice,
    RoundEnd,
    Complete
}

public sealed record CombatDie(int Value, bool Hit = false, bool Rerolled = false, bool Rout = false);
public sealed record CombatLogEntry(string Text, bool Subdued = false);
public sealed record CombatChoice(string Id, string Label, string? Detail = null);

public sealed class CompletedBattleLog
{
    public int BattleNumber { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public string SourceLabel { get; set; } = "Combat";
    public string EnemyName { get; set; } = "Enemy Host";
    public bool IsFinale { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public Dictionary<string, int> InitialPlayerUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> InitialEnemyUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> FinalPlayerUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> FinalEnemyUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CombatLogEntry> Entries { get; set; } = [];
}

public sealed class CombatState
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public bool IsTest { get; set; }
    public int? TestSeed { get; set; }
    public string EnemyName { get; set; } = "Enemy Host";
    public string SourceLabel { get; set; } = "Combat";
    public int Round { get; set; } = 1;
    public CombatStep Step { get; set; } = CombatStep.Setup;
    public CombatStep ResumeStep { get; set; } = CombatStep.Archers;
    public bool MayDisengage { get; set; }
    public bool NoDisengage { get; set; }
    public bool PlayerInitiated { get; set; }
    public bool IsHostileEcho { get; set; }
    public bool IsFinale { get; set; }
    public bool LockPlayerTactics { get; set; }
    public bool PlayerPriest { get; set; }
    public bool EnemyPriest { get; set; }
    public int PlayerLeadership { get; set; }
    public int InitialPlayerLeadership { get; set; }
    public int PlayerShields { get; set; }
    public int EnemyShields { get; set; }
    public Dictionary<string, int> PlayerUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> InitialPlayerUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> EnemyUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> InitialEnemyUnits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PlayerDevelopments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EnemyAdvancements { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PlayerTactics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CommittedTactics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RerolledTypesThisRound { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> BonusDiceThisRound { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SteadyAdvanceType { get; set; }
    public string? InspiredType { get; set; }
    public bool FeignedWithdrawalReady { get; set; }
    public bool CoveringFirePending { get; set; }
    public bool SteadyAdvanceAwardedThisRound { get; set; }
    public bool FirepotsAvailable { get; set; }
    public bool FirepotsResolved { get; set; }
    public bool ResolvingFirepots { get; set; }
    public bool ResolvingPursuit { get; set; }
    public int PursuitLaneIndex { get; set; }
    public bool PainkillerActive { get; set; }
    public List<CombatDie> PlayerRoll { get; } = [];
    public List<CombatDie> EnemyRoll { get; } = [];
    public string? CurrentUnitType { get; set; }
    public int PendingPlayerHits { get; set; }
    public int PendingEnemyHits { get; set; }
    public string? PendingAttackerType { get; set; }
    public string? PendingTargetType { get; set; }
    public bool ResolvingPlayerHits { get; set; }
    public List<CombatChoice> Choices { get; } = [];
    public string ChoicePrompt { get; set; } = string.Empty;
    public string? ChoiceTargetSide { get; set; }
    public List<CombatLogEntry> Log { get; } = [];
    public string? Outcome { get; set; }
    public bool AwaitingClose { get; set; }
    public Dictionary<string, int> ConvertedForPlayer { get; } = new(StringComparer.OrdinalIgnoreCase);
}
