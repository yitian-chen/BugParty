using UnityEngine;

namespace PartyGame
{
    public enum FishType
    {
        Common,
        Golden,
    }

    [CreateAssetMenu(fileName = "FishData", menuName = "PartyGame/FishData")]
    public class FishDataSO : ScriptableObject
    {
        public FishType type;
        public int score = 1;
        [Tooltip("Optional icon used by HUD / result screen.")]
        public Sprite icon;
    }
}
