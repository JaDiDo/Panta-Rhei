using Robust.Shared.Configuration;

namespace Content.Shared._Floof.Vore;

[CVarDefs]
public sealed class VoreCVars
{
    /// <summary>
    /// Enables or disables vore verbs and interactions.
    /// </summary>
    public static readonly CVarDef<bool> VoreEnabled =
        CVarDef.Create("game.vore_enabled", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<bool> VoreAllowPred =
        CVarDef.Create("vore.allowPred", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> VoreAllowPrey =
        CVarDef.Create("vore.allowPrey", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> VoreDigest =
        CVarDef.Create("vore.digest", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> VoreAllowSound =
        CVarDef.Create("vore.allowSound", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> VoreR =
        CVarDef.Create("vore.r", 255, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> VoreG =
        CVarDef.Create("vore.g", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> VoreB =
        CVarDef.Create("vore.b", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> VoreDigestSpeed =
        CVarDef.Create("vore.digestSpeed", 1, CVar.CLIENTONLY | CVar.ARCHIVE);
}