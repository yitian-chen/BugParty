using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// Static hand-off between the LAN menu scene and the game scene.
    /// The menu sets Mode + Address + Port before loading GameScene_PartyFishing,
    /// and NetworkedPartyBootstrap reads it to StartHost or StartClient.
    /// </summary>
    public static class LanBootstrapData
    {
        public enum StartMode
        {
            /// <summary>No mode set (e.g. hitting Play directly in the game scene).</summary>
            None,
            Host,
            Client,
        }

        public static StartMode Mode = StartMode.None;
        public static string Address = "127.0.0.1";
        public static ushort Port = 7777;

        /// <summary>Called once by the bootstrap after it consumes the data so a scene reload starts fresh.</summary>
        public static void Consume() => Mode = StartMode.None;
    }
}
