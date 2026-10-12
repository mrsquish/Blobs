namespace Blobs.Core
{
    /// <summary>
    /// Action map and action names in BlobsInput.inputactions. Keep in sync with the asset.
    /// </summary>
    public static class InputNames
    {
        public const string PlayerMap = "Player";
        public const string ResearchMap = "Research";

        public const string Move = "Player/Move";
        public const string Jump = "Player/Jump";
        public const string Interact = "Player/Interact";

        public const string ResearchNavigate = "Research/Navigate";
        public const string ResearchSubmit = "Research/Submit";
        public const string ResearchExit = "Research/Exit";
    }
}
