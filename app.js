"use strict";
(() => {
  const byId = id => document.getElementById(id);
  const play = byId("play-button"), canvas = byId("unity-canvas"), cover = byId("cover");
  const loading = byId("loading"), label = byId("loading-label"), progress = byId("loading-progress");
  const status = byId("player-status"), full = byId("fullscreen-button"), back = byId("return-button");
  let instance = null, busy = false, loaderPromise = null;
  function loadScript(url) {
    if (!loaderPromise) loaderPromise = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = url;
      script.onload = resolve;
      script.onerror = () => { script.remove(); loaderPromise = null; reject(new Error("Game loader could not be downloaded.")); };
      document.body.appendChild(script);
    });
    return loaderPromise;
  }
  async function start() {
    if (busy || instance) return;
    busy = true; play.disabled = true; cover.hidden = true; canvas.hidden = false; loading.hidden = false;
    progress.value = 0; label.textContent = "正在点亮太阳…"; status.textContent = "首次加载需要下载资源，请保持页面打开。";
    try {
      if (!window.WebAssembly) throw new Error("This browser does not support WebAssembly.");
      const response = await fetch("game-config.json", { cache: "no-cache" });
      if (!response.ok) throw new Error("Game configuration could not be downloaded.");
      const game = await response.json();
      await loadScript(game.loaderUrl);
      instance = await window.createUnityInstance(canvas, {
        ...game.config,
        devicePixelRatio: Math.min(window.devicePixelRatio || 1, 1.5),
        showBanner: (_, type) => { if (type === "error") status.textContent = "游戏加载遇到问题，也可下载 Windows 版继续体验。"; }
      }, value => {
        progress.value = value;
        label.textContent = "正在点亮太阳 · " + Math.round(value * 100) + "%";
      });
      loading.hidden = true; full.disabled = false; back.hidden = false;
      canvas.focus(); status.textContent = "点击游戏画面后操作；Esc 可暂停，全屏按钮可放大画面。";
    } catch (error) {
      console.error("Sunbearer could not start:", error);
      loading.hidden = true; canvas.hidden = true; cover.hidden = false;
      play.textContent = "重新加载"; status.textContent = "浏览器未能加载游戏，可重试，或下载 Windows 版。";
    } finally { busy = false; play.disabled = false; }
  }
  async function stop() {
    if (busy || !instance) return;
    busy = true; back.disabled = true; full.disabled = true;
    const current = instance;
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      await current.Quit();
    } catch (error) { console.error("Sunbearer could not exit cleanly:", error); }
    finally {
      instance = null; busy = false; back.disabled = false; back.hidden = true;
      canvas.hidden = true; cover.hidden = false; play.textContent = "再次试玩";
      status.textContent = "已返回封面。可以重新试玩，或下载 Windows 版。";
      play.focus();
    }
  }
  play.addEventListener("click", start);
  back.addEventListener("click", stop);
  window.addEventListener("sunbearer:quit", stop);
  full.addEventListener("click", () => { if (instance && !busy) { instance.SetFullscreen(1); canvas.focus(); } });
  canvas.addEventListener("pointerdown", () => canvas.focus());
  const tabs = [...document.querySelectorAll('[role="tab"]')];
  function selectTab(tab, focus) {
    for (const item of tabs) {
      const selected = item === tab;
      item.setAttribute("aria-selected", String(selected)); item.tabIndex = selected ? 0 : -1;
      byId(item.getAttribute("aria-controls")).hidden = !selected;
    }
    if (focus) tab.focus();
  }
  tabs.forEach((tab, index) => {
    tab.addEventListener("click", () => selectTab(tab, false));
    tab.addEventListener("keydown", event => {
      let next;
      if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
      if (event.key === "ArrowLeft") next = (index + tabs.length - 1) % tabs.length;
      if (event.key === "Home") next = 0;
      if (event.key === "End") next = tabs.length - 1;
      if (next !== undefined) { event.preventDefault(); selectTab(tabs[next], true); }
    });
  });
})();
