// Assets/QuantumUser/Game/RuntimeConfig.User.cs
using Photon.Deterministic;

namespace Quantum
{
    public partial class RuntimeConfig
    {
        public AssetRef<GameSettingsData> GameSettingsData;
        public AssetRef<EntityPrototype> BallPrototype;

        // This MUST be here so the JSON clone carries it into the sim
        public SelectedAbilities[] SelectedBySlot = new SelectedAbilities[6];

        // Optional: quick pretty print you can call manually for sanity
        public string DumpPretty() => UnityEngine.JsonUtility.ToJson(this, true);
    }
}
