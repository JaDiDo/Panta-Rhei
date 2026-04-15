using Content.Shared._Common.Consent;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.Body.Components;
using Content.Shared.Mind.Components;

public sealed class VoreSystem : EntitySystem
{
    [Dependency] private readonly SharedConsentSystem _consent = default!;
    
    public static readonly ProtoId<ConsentTogglePrototype> isPred = "PredVore";
    public static readonly ProtoId<ConsentTogglePrototype> isPrey = "PreyVore";

    public override void Initialize()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        var user = args.User;
        var target = args.Target;

        // no self activation
        if (user == target)
            return;

        //only when reachable & interactable
        if (!args.CanInteract || !args.CanAccess)
           return;

        //to avoid feeding yourself to items
        if (!EntityManager.HasComponent<BodyComponent>(target))
            return;

        if (!EntityManager.TryGetComponent<MindContainerComponent>(target, out var mindContainer) ||
        mindContainer.Mind == null)
            return;

        // Devour (pred → prey)
        if (_consent.HasConsent(user, isPred)
            && _consent.HasConsent(target, isPrey))
        {
            args.Verbs.Add(new Verb
            {
                Text = "Devour",
                Act = () => Log.Info("Devour clicked")
            });
        }

        // Insert Self (prey → pred)
        if (_consent.HasConsent(user, isPrey)
            && _consent.HasConsent(target, isPred))
        {
            args.Verbs.Add(new Verb
            {
                Text = "Insert Self",
                Act = () => Log.Info("Insert Self clicked")
            });
        }
    }
}