using UnityEngine;

namespace Sunbearer
{
    public sealed class SunBallVisual : MonoBehaviour
    {
        private const float CoronaScale = 2.1f;
        private const float SpinDegreesPerSecond = -125f;
        private static readonly int ChargeId = Shader.PropertyToID("_Charge");
        private static readonly int ExtentId = Shader.PropertyToID("_Extent");

        private Sprite ballSprite;
        private SpriteRenderer corona;
        private MaterialPropertyBlock coronaProperties;
        private float displayedCharge = -1f;

        internal static SunBallVisual Create(string name, Transform parent, Vector2 position,
            Sprite ballSprite, Material coronaMaterial, int order)
        {
            SpriteRenderer body = ArenaSprites.Create(name, parent, position, Vector2.one,
                ballSprite ? ballSprite : ArenaSprites.Disc,
                ballSprite ? Color.white : new Color(1f, 0.70f, 0.25f), order);
            var view = body.gameObject.AddComponent<SunBallVisual>();
            view.ballSprite = ballSprite;

            if (coronaMaterial)
            {
                view.corona = ArenaSprites.Create("Sun corona", body.transform, position,
                    Vector2.one * CoronaScale, ArenaSprites.Square, Color.white, order - 1);
                view.corona.sharedMaterial = coronaMaterial;
                view.coronaProperties = new MaterialPropertyBlock();
                view.coronaProperties.SetFloat(ExtentId, CoronaScale);
            }

            return view;
        }

        internal SunBallVisual CreateFlight(Transform parent, Vector2 position, float radius, float charge)
        {
            Material material = corona ? corona.sharedMaterial : null;
            SunBallVisual flight = Create("Flying sun", parent, position, ballSprite, material, 5);
            flight.transform.rotation = transform.rotation;
            flight.SetSize(radius, charge);
            return flight;
        }

        internal void SetSize(float radius, float charge)
        {
            float diameter = radius * 2f;
            transform.localScale = new Vector3(diameter, diameter, 1f);
            if (!corona || Mathf.Approximately(charge, displayedCharge))
                return;

            displayedCharge = charge;
            coronaProperties.SetFloat(ChargeId, Mathf.Clamp01(charge));
            corona.SetPropertyBlock(coronaProperties);
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, SpinDegreesPerSecond * Time.deltaTime);
        }
    }
}
