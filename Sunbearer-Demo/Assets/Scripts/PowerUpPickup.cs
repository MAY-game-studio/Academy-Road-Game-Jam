using UnityEngine;

namespace Sunbearer
{
    public sealed class PowerUpPickup : MonoBehaviour
    {
        private enum Kind
        {
            Turquoise,
            MiniBlackHole,
            SolarWings,
            LargeStardust
        }

        [SerializeField] private Kind kind;
        [SerializeField, Min(0.05f)] private float radius = 0.26f;

        internal float Radius => radius;

        internal string DisplayName => kind switch
        {
            Kind.Turquoise => "Turquoise",
            Kind.MiniBlackHole => "Mini Black Hole",
            Kind.SolarWings => "Solar Wings",
            Kind.LargeStardust => "Large Stardust",
            _ => string.Empty
        };

        internal bool TryCollect(PlayerController player)
        {
            float reach = player.BodyRadius + radius;
            if (!isActiveAndEnabled || player.Health <= 0f ||
                ((Vector2)transform.position - (Vector2)player.transform.position).sqrMagnitude > reach * reach)
                return false;

            switch (kind)
            {
                case Kind.Turquoise:
                    player.TryRestoreHealth(30f);
                    break;
                case Kind.MiniBlackHole:
                    player.ActivateBlackHole();
                    break;
                case Kind.SolarWings:
                    player.ActivateSolarWings();
                    break;
                case Kind.LargeStardust:
                    if (!player.TryAddBallCharge(0.50f))
                        return false;
                    break;
                default:
                    return false;
            }

            gameObject.SetActive(false);
            return true;
        }
    }
}
