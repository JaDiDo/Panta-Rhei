using Robust.Shared.Audio;
namespace Content.Shared._Floof.Vore;

[RegisterComponent, AutoGenerateComponentState]
public sealed partial class DevouredComponent : Component
{
    public bool AddedPressure;
    public bool AddedBreathing;
    public bool AddedTemperature;
    public bool AddedRadiation;

    public EntityUid? Stream;

    [DataField, AutoNetworkedField]
    public SoundSpecifier SoundBelly = new SoundPathSpecifier("/Audio/_Floof/Vore/stomach_loop.ogg");
}