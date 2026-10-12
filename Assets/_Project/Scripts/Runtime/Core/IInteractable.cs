using Blobs.Players;

namespace Blobs.Core
{
    /// <summary>
    /// Anything a player can use with the Interact button (terminals, doors, pickups...).
    /// Found by <see cref="PlayerMotor"/> via an overlap query, so the implementer needs a collider.
    /// </summary>
    public interface IInteractable
    {
        bool TryInteract(CoopPlayer player);
    }
}
