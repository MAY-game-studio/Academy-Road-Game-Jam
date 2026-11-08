using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sunbearer
{
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private CanvasGroup artwork;
        [SerializeField] private CanvasGroup foreground;
        [SerializeField] private string gameplayScene = "Playground";

        [Header("Fade timing")]
        [SerializeField, Min(0f)] private float foregroundDelay = 0.25f;
        [SerializeField, Min(0f)] private float foregroundFadeDuration = 0.75f;
        [SerializeField, Min(0f)] private float exitFadeDuration = 0.55f;

        private void Awake()
        {
            foreground.alpha = 0f;
            SetForegroundInput(false);
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(foregroundDelay);
            yield return Fade(foreground, 1f, foregroundFadeDuration);
            SetForegroundInput(true);
        }

        public void OnPlayPressed()
        {
            if (!foreground.interactable)
                return;

            if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
            {
                Debug.LogError($"The gameplay scene '{gameplayScene}' is missing from Build Settings.", this);
                return;
            }

            SetForegroundInput(false);
            StartCoroutine(EnterGameplay());
        }

        private IEnumerator EnterGameplay()
        {
            yield return Fade(artwork, 0f, exitFadeDuration);
            yield return SceneManager.LoadSceneAsync(gameplayScene);
        }

        private void SetForegroundInput(bool enabled)
        {
            foreground.interactable = enabled;
            foreground.blocksRaycasts = enabled;
        }

        private static IEnumerator Fade(CanvasGroup group, float target, float duration)
        {
            float start = group.alpha;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.SmoothStep(start, target, elapsed / duration);
                yield return null;
            }

            group.alpha = target;
        }
    }
}

