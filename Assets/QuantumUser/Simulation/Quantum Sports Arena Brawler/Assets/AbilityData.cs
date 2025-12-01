using Photon.Deterministic;
using UnityEngine;

namespace Quantum
{
    public unsafe partial class AbilityData : AssetObject
    {
        public FP InputBuffer = FP._0_10 + FP._0_05;
        public FP Delay = FP._0_10 + FP._0_05;
        public FP Duration = FP._0_25;
        public FP Cooldown = 5;

        public AbilityAvailabilityType AvailabilityType;
        public AbilityCastDirectionType CastDirectionType = AbilityCastDirectionType.Aim;
        public bool FaceCastDirection = true;
        public bool KeepVelocity = false;
        public bool StartCooldownAfterDelay = false;
        
        [Header("Unity")] [SerializeField] private GameObject _uiAbilityPrefab;

        public bool HasUIPrefab => _uiAbilityPrefab != null;
        public GameObject UIAbilityPrefab => _uiAbilityPrefab;

        public bool AllowConcurrent = false;

        [Header("Menu Info")]
        [Tooltip("Short description shown on the selection screen (one or two sentences).")]
        [TextArea] public string MenuShortDescription;

        [Tooltip("Comma- or line-separated bullet points of effects (e.g., 'Slips opponents\nStuns 0.5s').")]
        [TextArea] public string MenuEffects;

        public virtual Ability.AbilityState UpdateAbility(Frame frame, EntityRef entityRef, ref Ability ability)
        {
            return ability.Update(frame, entityRef);
        }

        public virtual void UpdateInput(Frame frame, ref Ability ability, bool inputWasPressed)
        {
            if (inputWasPressed)
            {
                ability.BufferInput(frame);
            }
        }

        public virtual bool TryActivateAbility(Frame frame, EntityRef entityRef, PlayerStatus* playerStatus, ref Ability ability)
        {
            if ((AvailabilityType == AbilityAvailabilityType.WithBall && !playerStatus->IsHoldingBall) ||
                (AvailabilityType == AbilityAvailabilityType.WithoutBall && playerStatus->IsHoldingBall))
            {
                return false;
            }

            if (ability.HasBufferedInput)
            {
                if (ability.TryActivateAbility(frame, entityRef, playerStatus->PlayerRef))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
