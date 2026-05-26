using UnityEngine;

// Any object the player can target with the Interact key. Doors implement this
// World items have their own pickup pipeline; PlayerInteractor checks for items first
// and falls back to IInteractable, so both work through the same Interact bind
public interface IInteractable
{
    // True if the player is currently allowed to interact with this object.
    // Pass false to short-circuit input and HUD prompts (e.g. door is locked)
    bool CanInteract(Transform interactor);

    // Short prompt shown in the HUD context line ("Open door", "Close door")
    // Return null or empty to suppress the prompt while still allowing interaction
    string GetInteractionPrompt();

    // Called when the Interact key is pressed while this object is the focused target
    void Interact(Transform interactor);
}
