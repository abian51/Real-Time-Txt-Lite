"use strict";
const $ = id => document.getElementById(id);
const sessions = new Map();
const modelReports = new Map();
const pending = new Map();
const languageOptions = [];
const languageCodes = new Set();
const languageNames = new Intl.DisplayNames(["zh-CN"], { type: "language" });
$("manual-source").value = "en";
$("manual-target").value = "zh";
let events, active, generation = 0, completed = 0, failures = 0, busy = false, preparing = false, closed = false;
let secondaryActive, secondaryBusy = false, secondaryModel, secondaryModelKey = "";
let nextSecondary = false;
let pageId = crypto.randomUUID();
let manualPending, manualReceipt, receiptTimer, leaseTimer, verificationController;
let connected = false, verifying = false;
let manualDirectionPending = true;
let checkingModels = false, downloadingModels = false, modelCheckTimer;
let manualModel, manualModelKey = "", manualAvailability = "checking", manualRevision = 0, manualPreparing = false, manualPreparationController;
let captionSelection = { sourceLanguage: "en", targetLanguage: "zh" };
let captionModel, captionModelKey = "", captionRevision = 0, captionPreparing = false, captionAvailability = "checking", captionController, captionCheckTimer;

function manualPair() { return { sourceLanguage: $("manual-source").value, targetLanguage: $("manual-target").value }; }
function pairKey(pair) { return `${pair.sourceLanguage}>${pair.targetLanguage}`; }
function captionPair(pair) {
  return pair.sourceLanguage === "en" && pair.targetLanguage === "zh" || pair.sourceLanguage === "zh" && pair.targetLanguage === "en";
}
function manualTranslator() {
  const pair = manualPair();
  return captionPair(pair) ? sessions.get(pair.sourceLanguage) : manualModelKey === pairKey(pair) ? manualModel : undefined;
}

function updateModelButton() {
  const downloadable = [...modelReports.values()].some(report => report.state === "downloadable");
  const downloading = downloadingModels || [...modelReports.values()].some(report => report.state === "loading" && report.detail === "下载中...");
  const button = $("prepare");
  const customCaption = !captionPair(captionSelection);
  const disabled = closed || preparing || verifying || checkingModels || captionPreparing;
  const text = closed ? "模型已停止" : customCaption ? captionPreparing ? "正在准备字幕模型..." : captionModel ? "已安装" :
    captionAvailability === "unavailable" ? "重新检查字幕语言对" : "下载所选字幕模型" : downloading ? "下载中..." : sessions.size === 2 ? "已安装" :
    preparing || checkingModels ? "正在检查模型..." : downloadable ? "点击下载微软翻译模型" : "重新检查模型";
  const title = customCaption ? `字幕 ${captionSelection.sourceLanguage} → ${captionSelection.targetLanguage}` : sessions.size === 2 ? "点击检查模型连通" : "检查并下载微软翻译模型";
  if (button.disabled !== disabled) button.disabled = disabled;
  if (button.textContent !== text) button.textContent = text;
  if (button.title !== title) button.title = title;
}

function updateManualControls() {
  updateModelButton();
  const occupied = manualPreparing || !!manualPending || !!activeManual() || !!manualReceipt?.waiting;
  $("manual-source").disabled = $("manual-target").disabled = closed || occupied;
  $("manual-swap").disabled = closed || occupied;
  $("manual-submit").disabled = closed || !connected || occupied ||
    (captionPair(manualPair()) && (preparing || checkingModels)) ||
    (!manualTranslator() && !["available", "downloadable", "downloading"].includes(manualAvailability));
  $("manual-submit").textContent = !manualTranslator() && ["downloadable", "downloading"].includes(manualAvailability) ? "下载模型并翻译" : "翻译";
  $("manual-copy").disabled = closed || !$("manual-output").value;
}

function renewLease() {
  clearTimeout(leaseTimer);
  leaseTimer = setTimeout(() => release("本机服务无响应，翻译已停止"), 30000);
}

async function post(path, data) {
  const response = await fetch(path, {
    method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ generation, ...data, pageId }), signal: AbortSignal.timeout(7000)
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response;
}

function status(state, detail, sourceLanguage = "") {
  if (closed) return;
  if (["ready", "loading", "downloadable", "unavailable", "error"].includes(state))
    modelReports.set(sourceLanguage, { state, detail, sourceLanguage });
  updateModelButton();
  if (state === "ready" && sessions.size < 2) {
    const issue = [...modelReports.values()].find(report => report.state === "error" || report.state === "unavailable");
    $("status").textContent = issue ? issue.detail : downloadingModels ? "下载中..." : "正在检查模型...";
  } else $("status").textContent = detail;
  if (connected && captionPair(manualPair()) && (!sourceLanguage || sourceLanguage === $("manual-source").value) &&
      !manualPending && !activeManual() && !manualReceipt &&
      ["ready", "loading", "downloadable", "unavailable", "error"].includes(state))
    $("manual-status").textContent = state === "ready" ? "就绪 · 自动记录开启" : detail;
  void post("/status", { state, detail, sourceLanguage }).catch(() => {
    $("connection").textContent = "本机服务未连接";
  });
}

async function findPair(sourceLanguage) {
  return findTranslationPair(sourceLanguage, sourceLanguage === "en" ? "zh" : "en");
}

async function findTranslationPair(sourceLanguage, targetLanguage) {
  const candidates = (sourceLanguage === "zh" ? ["zh", "zh-Hans"] : [sourceLanguage]).flatMap(source =>
    (targetLanguage === "zh" ? ["zh", "zh-Hans"] : [targetLanguage]).map(target => ({ sourceLanguage: source, targetLanguage: target })));
  const checked = [];
  for (const options of candidates) {
    const availability = await Translator.availability(options);
    checked.push(`${options.sourceLanguage} → ${options.targetLanguage}: ${availability}`);
    if (availability !== "unavailable") return { options, availability, checked };
  }
  return { options: candidates[0], availability: "unavailable", checked };
}

function reportCaption(state, detail, pair = captionSelection) {
  captionAvailability = state;
  $("caption-model-status").hidden = false;
  $("caption-model-status").textContent = `字幕 ${pair.sourceLanguage} → ${pair.targetLanguage}：${detail}`;
  void post("/status", { state, detail, ...pair }).catch(() => { $("connection").textContent = "本机服务未连接"; });
}

async function prepareSelectedCaption(allowDownload) {
  if (closed || captionPreparing || captionPair(captionSelection)) return;
  const revision = captionRevision;
  const pair = { ...captionSelection };
  captionPreparing = true;
  updateModelButton();
  clearTimeout(captionCheckTimer);
  try {
    if (captionModel) { reportCaption("ready", "已安装", pair); return; }
    if (!("Translator" in globalThis)) throw new Error("Translator API 不可用");
    const { options, availability } = await findTranslationPair(pair.sourceLanguage, pair.targetLanguage);
    if (closed || revision !== captionRevision) return;
    if (availability === "unavailable") { reportCaption("unavailable", "当前 Edge 不支持此字幕语言对", pair); return; }
    if (availability !== "available" && !allowDownload) {
      reportCaption(availability === "downloading" ? "loading" : "downloadable", availability === "downloading" ? "下载中..." : "请点击下载所选字幕模型", pair);
      if (availability === "downloading") captionCheckTimer = setTimeout(() => { void prepareSelectedCaption(false); }, 2000);
      return;
    }
    const controller = new AbortController();
    captionController = controller;
    reportCaption("loading", availability === "available" ? "正在加载..." : "下载中...", pair);
    const translator = await Translator.create({
      ...options, signal: controller.signal,
      monitor(monitor) {
        monitor.addEventListener("downloadprogress", event => {
          if (closed || revision !== captionRevision) return;
          const progress = Math.round(event.total ? event.loaded / event.total * 100 : event.loaded * 100);
          $("caption-model-status").textContent = `字幕 ${pair.sourceLanguage} → ${pair.targetLanguage}：下载中... ${progress}%`;
        });
      }
    });
    if (closed || revision !== captionRevision || controller.signal.aborted) { translator.destroy(); return; }
    captionModel = translator;
    captionModelKey = pairKey(pair);
    reportCaption("ready", "已安装", pair);
  } catch (error) {
    if (!closed && revision === captionRevision) reportCaption("error", `准备失败：${error.name}: ${error.message}`, pair);
  } finally {
    if (revision === captionRevision) { captionPreparing = false; updateManualControls(); void drain(); }
  }
}

function selectCaptionPair(data) {
  if (!data.sourceLanguage || !data.targetLanguage) return;
  const pair = { sourceLanguage: data.sourceLanguage, targetLanguage: data.targetLanguage };
  if (!languageCodes.has(pair.sourceLanguage) || !languageCodes.has(pair.targetLanguage) || pair.sourceLanguage === pair.targetLanguage) return;
  if (pairKey(pair) !== pairKey(captionSelection)) {
    captionRevision++;
    captionController?.abort();
    clearTimeout(captionCheckTimer);
    try { captionModel?.destroy(); } catch {}
    captionModel = undefined;
    captionModelKey = "";
    captionPreparing = false;
    captionAvailability = "checking";
    captionSelection = pair;
  }
  $("caption-model-status").hidden = captionPair(pair);
  if (!captionPair(pair)) void prepareSelectedCaption(false);
  updateModelButton();
}

async function prepare(allowDownload) {
  if (preparing || closed) return;
  clearTimeout(modelCheckTimer);
  let recheckDownload = false;
  preparing = true;
  updateModelButton();
  try {
    if (!("Translator" in globalThis)) throw new Error("Translator API 不可用，请检查 Edge 版本与企业策略");
    for (const sourceLanguage of ["en", "zh"]) {
      if (sessions.has(sourceLanguage)) continue;
      try {
      const { options, availability, checked } = await findPair(sourceLanguage);
      $(sourceLanguage).textContent = { available: "已安装", downloadable: "未安装", downloading: "下载中...", unavailable: "不可用" }[availability] || availability;
      if (availability === "unavailable") {
        status("unavailable", `语言对不可用：${checked.join("；")}。请检查企业策略和 Edge 模型`, sourceLanguage);
        continue;
      }
      if (availability !== "available" && !allowDownload) {
        if (availability === "downloading") recheckDownload = true;
        status(availability === "downloading" ? "loading" : "downloadable",
          availability === "downloading" ? "下载中..." : "点击下载微软翻译模型", sourceLanguage);
        continue;
      }
      if (availability !== "available") downloadingModels = true;
      $(sourceLanguage).textContent = availability === "available" ? "已安装" : "下载中...";
      status("loading", availability === "available" ? "已安装" : "下载中...", sourceLanguage);
      const translator = await Translator.create({
        ...options,
        monitor(monitor) {
          monitor.addEventListener("downloadprogress", event => {
            const progress = Math.round(event.total ? event.loaded / event.total * 100 : event.loaded * 100);
            $(sourceLanguage).textContent = `下载中... ${progress}%`;
          });
        }
      });
      if (closed) { translator.destroy(); return; }
      sessions.set(sourceLanguage, translator);
      $(sourceLanguage).textContent = "已安装";
      status("ready", "已安装", sourceLanguage);
      } catch (error) {
        $(sourceLanguage).textContent = `${error.name}: ${error.message}`;
        status("error", `${sourceLanguage}: ${error.name}: ${error.message}`, sourceLanguage);
      }
    }
  } catch (error) {
    status("error", `${error.name}: ${error.message}`);
  } finally {
    preparing = false;
    downloadingModels = false;
    updateManualControls();
    if (!closed) {
      const issue = [...modelReports.values()].find(report => report.state === "error" || report.state === "unavailable");
      $("status").textContent = issue ? issue.detail : $("prepare").textContent;
      if (recheckDownload) modelCheckTimer = setTimeout(() => { void prepare(false); }, 2000);
    }
    void drain();
  }
}

function activeTasks() { return [active, secondaryActive].filter(Boolean); }
function activeManual() { return activeTasks().find(work => work.kind === "manual"); }

function drain() {
  const preferred = nextSecondary;
  void drainLane(preferred);
  void drainLane(!preferred);
}

async function drainLane(secondary) {
  if ((secondary ? secondaryBusy : busy) || verifying || checkingModels || closed) return;
  const task = manualPending || [...pending.values()].find(item => !activeTasks().some(work => work.segmentId === item.segmentId));
  if (!task) return;
  const manual = task.kind === "manual";
  let translator = manual ? manualTranslator() : captionPair(task) ? sessions.get(task.sourceLanguage) : captionModelKey === pairKey(task) ? captionModel : undefined;
  if (!translator) return;
  if (secondary) secondaryBusy = true;
  else busy = true;
  nextSecondary = !secondary;
  try {
    do {
      if (manual) manualPending = null;
      else pending.delete(task.segmentId);
      const controller = new AbortController();
      const deadline = setTimeout(() => controller.abort(), 15000);
      const work = { ...task, controller };
      if (secondary) secondaryActive = work;
      else active = work;
      updateManualControls();
      const start = performance.now();
      try {
        if (secondary) {
          if (secondaryModelKey !== pairKey(task)) {
            const { options, availability } = await findTranslationPair(task.sourceLanguage, task.targetLanguage);
            if (availability !== "available") throw new Error("SecondaryModelUnavailable");
            if (controller.signal.aborted) throw Object.assign(new Error("aborted"), { name: "AbortError" });
            const model = await Translator.create({ ...options, signal: controller.signal });
            if (closed || controller.signal.aborted || work.cancelled || task.generation !== generation) {
              model.destroy();
              throw Object.assign(new Error("aborted"), { name: "AbortError" });
            }
            try { secondaryModel?.destroy(); } catch {}
            secondaryModel = model;
            secondaryModelKey = pairKey(task);
          }
          translator = secondaryModel;
        }
        const translatedText = await translator.translate(task.text, { signal: controller.signal });
        if (closed || work.cancelled || task.generation !== generation) continue;
        if (controller.signal.aborted) throw Object.assign(new Error("aborted"), { name: "AbortError" });
        if (typeof translatedText !== "string" || !translatedText.trim() || translatedText.length > 8000) throw new Error("InvalidTranslation");
        const repeatedLetters = translatedText.match(/(\p{L})\1{23,}/gu) || [];
        if (repeatedLetters.some(run => run.length > Math.max(24, task.text.length * 4)))
          throw Object.assign(new Error("RepetitiveTranslation"), { name: "RepetitiveTranslation" });
        if (manual) {
          $("manual-output").value = translatedText;
          manualReceipt = { requestId: task.requestId, waiting: true };
          $("manual-status").textContent = "翻译完成 · 正在确认记录";
          clearTimeout(receiptTimer);
          receiptTimer = setTimeout(() => {
            if (manualReceipt?.requestId !== task.requestId) return;
            manualReceipt.waiting = false;
            $("manual-status").textContent = "翻译完成 · 记录尚未确认，请检查会议文件";
            updateManualControls();
          }, 10000);
        }
        await post(manual ? "/manual-result" : "/result", {
          ...task, translatedText, durationMs: performance.now() - start, status: "ok"
        });
        completed++;
        $("count").textContent = completed;
        $("latency").textContent = `${Math.round(performance.now() - start)} ms`;
      } catch (error) {
        if (secondary && secondaryModel === translator && controller.signal.aborted) {
          try { secondaryModel.destroy(); } catch {}
          secondaryModel = undefined;
          secondaryModelKey = "";
        }
        if (!closed && task.generation === generation && !work.cancelled) {
          if (manual) {
            clearTimeout(receiptTimer);
            if (manualReceipt?.requestId === task.requestId) manualReceipt.waiting = false;
            $("manual-status").textContent = manualReceipt?.requestId === task.requestId
              ? "翻译完成 · 记录提交未确认，请检查会议文件" : `翻译未完成：${error.name}`;
          } else {
            await post("/result", { ...task, status: "error", errorCode: error.name, durationMs: performance.now() - start }).catch(() => {});
            if (error.name !== "AbortError") status("translation_error", `翻译失败：${error.name}`, task.sourceLanguage);
          }
        }
      } finally {
        clearTimeout(deadline);
        if (secondary) secondaryActive = null;
        else active = null;
        updateManualControls();
      }
    } while (false);
  } finally {
    if (secondary) secondaryBusy = false;
    else busy = false;
    updateModelButton();
    drain();
  }
}

function reset(nextGeneration) {
  if (manualPending || activeManual() || manualReceipt?.waiting)
    $("manual-status").textContent = "会话已切换，未确认的记录请检查会议文件";
  manualPending = null;
  manualRevision++;
  manualPreparationController?.abort();
  manualPreparing = false;
  manualReceipt = null;
  clearTimeout(receiptTimer);
  verificationController?.abort();
  generation = nextGeneration;
  nextSecondary = false;
  pending.clear();
  for (const work of activeTasks()) { work.cancelled = true; work.controller.abort(); }
  try { secondaryModel?.destroy(); } catch {}
  secondaryModel = undefined;
  secondaryModelKey = "";
}

function release(message, closeWindow = false) {
  if (closed) return;
  closed = true;
  connected = false;
  clearTimeout(leaseTimer);
  clearTimeout(modelCheckTimer);
  clearTimeout(captionCheckTimer);
  captionRevision++;
  captionController?.abort();
  try { captionModel?.destroy(); } catch {}
  captionModel = undefined;
  captionModelKey = "";
  reset(generation);
  events?.close();
  for (const translator of sessions.values()) {
    try { translator.destroy(); } catch {}
  }
  sessions.clear();
  try { manualModel?.destroy(); } catch {}
  manualModel = undefined;
  manualModelKey = "";
  modelReports.clear();
  $("status").textContent = message;
  $("connection").textContent = "已断开";
  $("prepare").disabled = true;
  $("en").textContent = "已停止";
  $("zh").textContent = "已停止";
  $("verify").disabled = true;
  $("manual-status").textContent = message;
  updateManualControls();
  if (closeWindow) window.close();
}

function connect() {
  renewLease();
  events?.close();
  events = new EventSource("/events");
  events.onopen = () => { failures = 0; renewLease(); $("connection").textContent = "已连接"; };
  events.addEventListener("ping", () => {
    if (closed) return;
    renewLease();
    void post("/heartbeat", {}).catch(() => { $("connection").textContent = "连接中断"; });
  });
  events.addEventListener("control", event => {
    const data = JSON.parse(event.data);
    if (closed) return;
    renewLease();
    reset(data.generation);
    if (data.shutdown) {
      release("应用已退出", true);
    } else {
      connected = true;
      selectCaptionPair(data);
      if (manualDirectionPending && languageCodes.has(data.sourceLanguage) && languageCodes.has(data.targetLanguage) && data.sourceLanguage !== data.targetLanguage) {
        manualDirectionPending = false;
        $("manual-source").value = data.sourceLanguage;
        $("manual-target").value = data.targetLanguage;
        manualRevision++;
      }
      $("manual-status").textContent = manualTranslator()
        ? "就绪 · 自动记录开启" : "当前方向模型未就绪";
      for (const report of [...modelReports.values()]) status(report.state, report.detail, report.sourceLanguage);
      updateManualControls();
      void checkManualPair();
    }
  });
  events.addEventListener("task", event => {
    const task = JSON.parse(event.data);
    if (task.generation !== generation || closed) return;
    renewLease();
    const running = activeTasks().find(work => work.segmentId === task.segmentId);
    if (running?.revision >= task.revision) return;
    if (running) { running.cancelled = true; running.controller.abort(); }
    const older = pending.get(task.segmentId);
    if (older && older.revision >= task.revision) return;
    pending.set(task.segmentId, task);
    while (pending.size > 12) pending.delete(pending.keys().next().value);
    void drain();
  });
  events.addEventListener("cancel", event => {
    const data = JSON.parse(event.data);
    for (const id of data.segmentIds) {
      pending.delete(id);
      for (const work of activeTasks()) {
        if (work.segmentId === id) { work.cancelled = true; work.controller.abort(); }
      }
    }
  });
  events.addEventListener("verify", () => { void verifyModels(); });
  events.addEventListener("manual-recorded", event => {
    const data = JSON.parse(event.data);
    if (closed || manualReceipt?.requestId !== data.requestId) return;
    clearTimeout(receiptTimer);
    manualReceipt.waiting = false;
    const messages = { saved: "翻译完成 · 已写入会议记录", locked: "翻译完成 · 会话已锁定，未记录", full: "翻译完成 · 记录队列已满，未记录", invalid: "翻译完成 · 记录被拒绝" };
    $("manual-status").textContent = messages[data.state] || "翻译完成 · 记录未确认";
    updateManualControls();
  });
  events.onerror = () => {
    if (closed) return;
    reset(generation);
    connected = false;
    updateManualControls();
    $("connection").textContent = "连接中断，正在重试";
    if (++failures >= 6) release("本机服务已断开，翻译已停止");
  };
}

$("prepare").onclick = async () => {
  if (closed || preparing || verifying || checkingModels) return;
  if (busy || secondaryBusy) {
    $("status").textContent = "正在翻译，请在当前翻译完成后操作模型";
    return;
  }
  if (!captionPair(captionSelection)) { await prepareSelectedCaption(true); return; }
  checkingModels = true;
  updateModelButton();
  try {
    const alreadyInstalled = sessions.size === 2;
    await prepare(true);
    if (alreadyInstalled && !closed && sessions.size === 2) await verifyModels();
  } finally { checkingModels = false; updateManualControls(); void drain(); }
};
$("reconnect").onclick = () => location.reload();
$("manual-model-download").onclick = async () => {
  const button = $("manual-model-download");
  if (button.disabled) return;
  const feedback = $("model-download-status");
  button.disabled = true;
  feedback.hidden = false;
  feedback.textContent = "正在调用 Edge...";
  try {
    await post("/open-model-download", {});
    feedback.textContent = "已调用 Edge 打开微软官方模型页面";
  } catch {
    feedback.textContent = "无法打开 Edge，请正常退出并重新启动字幕程序后重试；仍失败请检查 Edge 安装或组织策略";
  } finally { button.disabled = false; }
};
async function checkManualPair() {
  const revision = manualRevision;
  const pair = manualPair();
  manualAvailability = "checking";
  updateManualControls();
  try {
    if (pair.sourceLanguage === pair.targetLanguage) {
      manualAvailability = "unavailable";
      $("manual-status").textContent = "请选择不同的源语言和目标语言";
    } else if (manualTranslator()) {
      manualAvailability = "available";
      $("manual-status").textContent = "就绪 · 自动记录开启";
    } else {
      const result = await findTranslationPair(pair.sourceLanguage, pair.targetLanguage);
      if (closed || revision !== manualRevision) return;
      manualAvailability = result.availability;
      $("manual-status").textContent = { available: "就绪 · 自动记录开启", downloadable: "所选翻译模型尚未下载", downloading: "所选翻译模型下载中...", unavailable: "当前 Edge 不支持此翻译语言对 (unavailable)" }[result.availability] || "无法确认翻译模型";
    }
  } catch (error) {
    if (closed || revision !== manualRevision) return;
    manualAvailability = "unavailable";
    $("manual-status").textContent = `翻译语言对检查失败：${error.name}`;
  } finally { if (!closed && revision === manualRevision) updateManualControls(); }
}

function changeManualPair() {
  if (manualPreparing || manualPending || activeManual() || manualReceipt?.waiting) return;
  manualDirectionPending = false;
  manualRevision++;
  try { manualModel?.destroy(); } catch {}
  manualModel = undefined;
  manualModelKey = "";
  manualReceipt = null;
  $("manual-output").value = "";
  void checkManualPair();
  updateManualControls();
}
$("manual-source").onchange = changeManualPair;
$("manual-target").onchange = changeManualPair;
$("manual-swap").onclick = () => {
  if ($("manual-swap").disabled) return;
  const source = $("manual-source").value;
  $("manual-source").value = $("manual-target").value;
  $("manual-target").value = source;
  changeManualPair();
};
$("manual-form").onsubmit = async event => {
  event.preventDefault();
  updateManualControls();
  if ($("manual-submit").disabled) return;
  const text = $("manual-input").value.trim();
  if (!text || text.length > 4000) { $("manual-status").textContent = "请输入 1–4000 个字符"; return; }
  const pair = manualPair();
  if (!languageCodes.has(pair.sourceLanguage) || !languageCodes.has(pair.targetLanguage) || pair.sourceLanguage === pair.targetLanguage) return;
  const revision = manualRevision;
  const taskGeneration = generation;
  if (!manualTranslator()) {
    manualPreparing = true;
    const controller = new AbortController();
    manualPreparationController = controller;
    updateManualControls();
    try {
      const result = await findTranslationPair(pair.sourceLanguage, pair.targetLanguage);
      if (closed || revision !== manualRevision || controller.signal.aborted) return;
      if (result.availability === "unavailable") throw new Error("UnsupportedLanguagePair");
      $("manual-status").textContent = result.availability === "available" ? "正在加载翻译模型" : "翻译模型下载中...";
      const translator = await Translator.create({ ...result.options, signal: controller.signal, monitor(monitor) {
        monitor.addEventListener("downloadprogress", progress => {
          if (closed || revision !== manualRevision || controller.signal.aborted) return;
          const percentage = Math.round(progress.total ? progress.loaded / progress.total * 100 : progress.loaded * 100);
          $("manual-status").textContent = `翻译模型下载中... ${percentage}%`;
        });
      } });
      if (closed || revision !== manualRevision || controller.signal.aborted || taskGeneration !== generation) { translator.destroy(); return; }
      if (captionPair(pair)) {
        sessions.set(pair.sourceLanguage, translator);
        $(pair.sourceLanguage).textContent = "已安装";
        status("ready", "已安装", pair.sourceLanguage);
      }
      else { manualModel = translator; manualModelKey = pairKey(pair); }
    } catch (error) {
      if (!closed && revision === manualRevision && !controller.signal.aborted) $("manual-status").textContent = `翻译模型未就绪：${error.message}`;
      return;
    } finally {
      if (revision === manualRevision) { manualPreparing = false; manualPreparationController = undefined; updateManualControls(); }
    }
  }
  if (closed || revision !== manualRevision || taskGeneration !== generation) return;
  manualPending = { kind: "manual", requestId: crypto.randomUUID(), generation, text, ...pair };
  manualReceipt = null;
  $("manual-output").value = "";
  $("manual-status").textContent = (busy && secondaryBusy) || verifying ? "等待当前翻译完成" : "正在翻译";
  updateManualControls();
  void drain();
};
$("manual-clear").onclick = () => {
  if (manualPreparing) manualRevision++;
  manualPreparationController?.abort();
  manualPreparing = false;
  manualPending = null;
  const manual = activeManual();
  if (manual) { manual.cancelled = true; manual.controller.abort(); }
  manualReceipt = null;
  clearTimeout(receiptTimer);
  $("manual-input").value = "";
  $("manual-output").value = "";
  $("manual-status").textContent = "已清空";
  updateManualControls();
};
$("manual-copy").onclick = async () => {
  if (closed) return;
  try { await navigator.clipboard.writeText($("manual-output").value); $("manual-status").textContent = "译文已复制"; }
  catch { $("manual-status").textContent = "无法写入剪贴板，请选择译文复制"; }
};
async function verifyModels() {
  if (closed) return;
  if (sessions.size !== 2 || busy || secondaryBusy || verifying) {
    $("verification").textContent = "模型验证：请先准备双向模型并暂停字幕翻译";
    status("verification_failed", "模型验证未执行：请先准备双向模型并暂停字幕翻译");
    return;
  }
  if ($("verify").disabled) return;
  verifying = true;
  updateModelButton();
  $("status").textContent = "正在检查模型连通";
  const controller = new AbortController();
  verificationController = controller;
  const deadline = setTimeout(() => controller.abort(), 30000);
  $("verify").disabled = true;
  const results = [];
  try {
    for (const [source, text] of [["en", "The meeting starts at nine tomorrow."], ["zh", "会议明天上午九点开始。"]]) {
      const start = performance.now();
      const translation = await sessions.get(source).translate(text, { signal: controller.signal });
      if (closed || controller.signal.aborted) return;
      if (!translation.trim()) throw new Error("EmptyTranslation");
      const duration = Math.round(performance.now() - start);
      results.push(`${source}: ${text}\n${translation}\n${duration} ms`);
    }
    $("verification").textContent = "模型验证：双向调用成功（固定样句，非音频链路）\n" + results.join("\n\n");
    status("verified", "双向模型连通正常");
  } catch (error) {
    if (closed || controller.signal.aborted) return;
    $("verification").textContent = `模型验证：未通过 ${error.name}`;
    status("verification_failed", `模型连通检查失败：${error.name}`);
  } finally { clearTimeout(deadline); verificationController = null; verifying = false; $("verify").disabled = closed; updateModelButton(); void drain(); }
}
$("verify").onclick = () => { void verifyModels(); };
window.addEventListener("pagehide", () => release("页面已关闭"));
async function initialize() {
  try {
    const response = await fetch("/languages.json", { credentials: "same-origin" });
    if (!response.ok) throw new Error("语言目录加载失败");
    languageOptions.push(...await response.json());
    for (const [code] of languageOptions) languageCodes.add(code);
    const markup = languageOptions.map(([code, name]) => {
      const label = { en: "英文", zh: "中文（简体）", ja: "日文" }[code] || `${languageNames.of(code)} / ${name}`;
      return `<option value="${code}">${label}</option>`;
    }).join("");
    $("manual-source").innerHTML = $("manual-target").innerHTML = markup;
    $("manual-source").value = "en";
    $("manual-target").value = "zh";
    connect();
    await prepare(false);
    await checkManualPair();
  } catch (error) { release(`语言目录加载失败：${error.message}`); }
}
void initialize();