using UnityEngine;

/// <summary>
/// Object the player can target with the interact key. World items use a separate pickup pipeline;
/// <see cref="PlayerInteractor"/> checks items first, then falls back to this interface.
/// </summary>
public interface IInteractable
{
    /// <summary>Whether the player may interact right now (e.g. door is not locked).</summary>
    bool CanInteract(Transform interactor);

    /// <summary>HUD prompt text such as "Open door"; null or empty suppresses the prompt.</summary>
    string GetInteractionPrompt();

    /// <summary>Called when interact is pressed while this object is the focused target.</summary>
    void Interact(Transform interactor);
}
