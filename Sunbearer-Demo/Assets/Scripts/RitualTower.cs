using System;
using UnityEngine;

namespace Sunbearer
{
    [DisallowMultipleComponent]
    public sealed class RitualTower : MonoBehaviour
    {
        [SerializeField] private Texture2D icon;
        [SerializeField, Min(1)] private int energyRequired = 100;

        internal event Action<RitualTower> FullBallHit;

        internal Texture2D Icon => icon;
        internal int Energy { get; private set; }
        internal int EnergyRequired => energyRequired;
        internal bool IsComplete => Energy >= energyRequired;

        internal void AddEnergy(int amount)
        {
            if (amount <= 0 || IsComplete)
                return;

            Energy += Mathf.Min(amount, energyRequired - Energy);
        }

        internal static void ReportFullBallHit(Collider2D surface)
        {
            if (surface.TryGetComponent(out RitualTower tower) && tower.isActiveAndEnabled)
                tower.FullBallHit?.Invoke(tower);
        }
    }
}
