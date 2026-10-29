"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

function harness(overrides = {}) {
  const elements = new Map();
  const requests = [];
  const calls = [];
  const streams = [];
  const timers = new Map();
  class Source {
    constructor() { this.listeners = new Map(); streams.push(this); }
    addEventListener(name, callback) { this.listeners.set(name, callback); }
    emit(name, data) { this.listeners.get(name)({ data: JSON.stringify(data) }); }
    close() { this.closed = true; }
  }
  const context = {
    document: { getElementById(id) { if (!elements.has(id)) elements.set(id, { value: "" }); return elements.get(id); } },
    crypto: { randomUUID: () => "test-page" },
    performance, AbortController, AbortSignal,
    setTimeout(callback, delay) { const timer = setTimeout(callback, delay); timer.unref(); timers.set(timer, { callback, delay }); return timer; },
    clearTimeout(timer) { timers.delete(timer); clearTimeout(timer); },
    EventSource: Source,
    window: { addEventListener() {}, close() {} }, location: { reload() {} },
    fetch: async (url, options) => {
      if (url === "/languages.json") return overrides.catalog || { ok: true, json: async () => JSON.parse(fs.readFileSync(path.join(__dirname, "..", "web", "languages.json"), "utf8")) };
      const body = JSON.parse(options.body);
      requests.push({ url, body });
      if (overrides.request) return overrides.request(url, body);
      return { ok: true };
    },
    Translator: {
      availability: overrides.availability || (async () => "available"),
      create: overrides.create || (async () => ({
        destroy() {},
        translate(text, { signal }) {
          return new Promise((resolve, reject) => {
            calls.push({ text, resolve, reject, signal });
            signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" })), { once: true });
          });
        }
      }))
    }
  };
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "..", "web", "app.js"), "utf8"), context);
  return { elements, requests, calls, streams, context, timers };
}

const flush = () => new Promise(resolve => setImmediate(resolve));
const task = (id, revision, generation = 4) => ({
  segmentId: id, revision, generation, text: `segment ${id} rev ${revision}`,
  sourceLanguage: "en", targetLanguage: "zh", timestamp: new Date().toISOString()
});

test("manual model download is an enabled native command beside model preparation", () => {
  const html = fs.readFileSync(path.join(__dirname, "..", "web", "index.html"), "utf8");
  assert.match(html, /<button id="prepare"[^>]*>[^<]*<\/button>\s*<button id="manual-model-download"/);
  const button = html.match(/<button id="manual-model-download"[^>]*>[^<]*<\/button>/)[0];
  assert.match(button, /手动下载微软模型<\/button>/);
  assert.doesNotMatch(button, /\bdisabled\b|\bhidden\b|microsoft-edge:/);
});

test("installed models do not prevent requesting the official model page", async () => {
  const app = harness();
  await flush();
  assert.equal(app.elements.get("prepare").textContent, "已安装");
  const button = app.elements.get("manual-model-download");
  await button.onclick();
  assert.equal(app.requests.filter(request => request.url === "/open-model-download").length, 1);
  assert.equal(button.disabled, false);
  assert.match(app.elements.get("model-download-status").textContent, /已调用 Edge/);
});

test("manual model page launch failure is visible and allows retry", async () => {
  const app = harness({ request: async () => ({ ok: false, status: 503 }) });
  await flush();
  const button = app.elements.get("manual-model-download");
  await button.onclick();
  assert.equal(button.disabled, false);
  assert.equal(app.elements.get("model-download-status").hidden, false);
  assert.match(app.elements.get("model-download-status").textContent, /无法打开 Edge.*重新启动/);
});

test("manual model page launch blocks duplicate clicks only while the request is pending", async () => {
  let finish;
  const app = harness({ request: (url, body) => url === "/open-model-download" ? new Promise(resolve => { finish = resolve; }) : Promise.resolve({ ok: true, json: async () => ({ ...body, state: "available", missing: [] }) }) });
  await flush();
  const button = app.elements.get("manual-model-download");
  const opening = button.onclick();
  assert.equal(button.disabled, true);
  await button.onclick();
  assert.equal(app.requests.filter(request => request.url === "/open-model-download").length, 1);
  finish({ ok: true });
  await opening;
  assert.equal(button.disabled, false);
});

test("new revision aborts active translation and only latest result returns", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  events.emit("task", task(1, 1));
  assert.equal(app.calls.length, 1);
  events.emit("task", task(1, 2));
  assert.equal(app.calls[0].signal.aborted, true);
  await flush();
  assert.equal(app.calls.length, 2);
  app.calls[1].resolve("latest translation");
  await flush();
  const results = app.requests.filter(request => request.url === "/result" && request.body.status === "ok");
  assert.equal(results.length, 1);
  assert.equal(results[0].body.revision, 2);
});

test("model button stays unchanged across repeated subtitle translations", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  const button = app.elements.get("prepare");
  const writes = [];
  for (const property of ["disabled", "textContent", "title"]) {
    let value = button[property];
    Object.defineProperty(button, property, {
      get: () => value,
      set: next => { writes.push({ property, next }); value = next; }
    });
  }
  for (let index = 0; index < 3; index++) {
    events.emit("task", task(index + 1, 1));
    assert.equal(button.textContent, "已安装");
    assert.equal(button.disabled, false);
    await button.onclick();
    assert.match(app.elements.get("status").textContent, /当前翻译完成后/);
    assert.equal(app.calls.length, index + 1);
    assert.equal(app.calls[index].signal.aborted, false);
    app.calls[index].resolve("translation");
    await flush();
  }
  assert.deepEqual(writes, []);
});

test("direction generation change rejects stale task and cancels work", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  events.emit("task", task(1, 1));
  events.emit("control", { generation: 5 });
  assert.equal(app.calls[0].signal.aborted, true);
  await flush();
  events.emit("task", task(2, 1, 4));
  assert.equal(app.calls.length, 1);
});

test("pending translations bounded and cancelled by segment eviction", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  for (let id = 1; id <= 40; id++) events.emit("task", task(id, 1));
  assert.equal(vm.runInContext("pending.size", app.context), 12);
  assert.equal(app.calls.length, 1);
  events.emit("cancel", { segmentIds: [1, 40] });
  assert.equal(app.calls[0].signal.aborted, true);
  assert.equal(vm.runInContext("pending.has(40)", app.context), false);
  events.emit("control", { generation: 5 });
  await flush();
});

test("SSE retry stops after six errors and shutdown releases sessions", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  for (let attempt = 0; attempt < 6; attempt++) events.onerror();
  assert.equal(events.closed, true);
  assert.equal(vm.runInContext("sessions.size", app.context), 0);
  assert.equal(vm.runInContext("closed", app.context), true);
  assert.equal(app.elements.get("prepare").disabled, true);
  events.emit("control", { generation: 5, shutdown: true });
  assert.equal(vm.runInContext("sessions.size", app.context), 0);
});

test("shutdown cancels active work and releases both models even when tab remains open", async () => {
  let destroyed = 0;
  const app = harness({ create: async () => ({
    destroy() { destroyed++; },
    translate: (_text, { signal }) => new Promise((_resolve, reject) => {
      signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" })), { once: true });
    })
  }) });
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  app.streams[0].emit("task", task(1, 1));
  app.streams[0].emit("control", { generation: 5, shutdown: true });
  await flush();
  assert.equal(destroyed, 2);
  assert.equal(vm.runInContext("pending.size", app.context), 0);
  assert.equal(vm.runInContext("sessions.size", app.context), 0);
  assert.equal(app.elements.get("status").textContent, "应用已退出");
  assert.equal(app.requests.filter(item => item.url === "/result").length, 0);
});

test("Chinese script tag fallback prepares both canonical directions", async () => {
  const checked = [];
  const app = harness({ availability: async options => {
    checked.push(options);
    return options.sourceLanguage === "zh" || options.targetLanguage === "zh" ? "unavailable" : "available";
  } });
  await flush();
  assert.equal(checked.length, 4);
  assert.equal(vm.runInContext("sessions.size", app.context), 2);
  assert.deepEqual(app.requests.filter(item => item.body.state === "ready").map(item => item.body.sourceLanguage), ["en", "zh"]);
});

test("failed direction preparation does not discard the healthy direction", async () => {
  const app = harness({ create: async options => {
    if (options.sourceLanguage === "zh") throw new Error("model not ready");
    return { destroy() {} };
  } });
  await flush();
  assert.equal(vm.runInContext("sessions.has('en')", app.context), true);
  const error = app.requests.find(item => item.body.state === "error");
  assert.equal(error.body.sourceLanguage, "zh");
});

test("translation failure retains model readiness and allows later tasks", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  events.emit("task", task(1, 1));
  app.calls[0].reject(Object.assign(new Error("temporary"), { name: "OperationError" }));
  await flush();
  const error = app.requests.find(item => item.body.state === "translation_error");
  assert.equal(error.body.sourceLanguage, "en");
  assert.equal(vm.runInContext("sessions.size", app.context), 2);
  events.emit("task", task(2, 1));
  app.calls[1].resolve("recovered");
  await flush();
  assert.ok(app.requests.some(item => item.body.status === "ok" && item.body.segmentId === 2));
});

test("handshake and reconnect replay unavailable model states in the current generation", async () => {
  const app = harness({ availability: async () => "unavailable" });
  await flush();
  assert.equal(vm.runInContext("sessions.size", app.context), 0);
  for (const generation of [4, 5]) {
    app.streams[0].emit("control", { generation });
    await flush();
    const reports = app.requests.filter(item => item.body.generation === generation && item.body.state === "unavailable");
    assert.deepEqual(reports.map(item => item.body.sourceLanguage), ["en", "zh"]);
    assert.ok(reports.every(item => item.body.detail.includes("unavailable")));
    assert.equal(app.elements.get("manual-submit").disabled, true);
    assert.ok(app.elements.get("manual-status").textContent.includes("unavailable"));
  }
});

test("manual translation shares serial models and waits for durable recording acknowledgement", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  events.emit("task", task(1, 1));
  app.context.document.getElementById("manual-input").value = "Typed meeting text";
  app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  assert.equal(app.calls.length, 1);
  app.calls[0].resolve("caption translation");
  await flush();
  assert.equal(app.calls[1].text, "Typed meeting text");
  app.calls[1].resolve("manual translation");
  await flush();
  const result = app.requests.find(item => item.url === "/manual-result");
  assert.equal(result.body.text, "Typed meeting text");
  assert.equal(app.elements.get("manual-output").value, "manual translation");
  assert.equal(app.elements.get("manual-submit").disabled, true);
  events.emit("manual-recorded", { requestId: result.body.requestId, state: "saved" });
  assert.equal(app.elements.get("manual-status").textContent, "翻译完成 · 已写入会议记录");
  assert.equal(app.elements.get("manual-submit").disabled, false);
});

test("manual recording needs acknowledgement but copying has no privacy gate", async () => {
  const app = harness();
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4, recordingEnabled: false });
  assert.equal(app.elements.get("manual-status").textContent, "就绪 · 自动记录开启");
  const submit = () => app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  app.context.document.getElementById("manual-input").value = "x".repeat(4001);
  submit();
  assert.equal(app.calls.length, 0);
  app.elements.get("manual-input").value = "typed text";
  submit(); submit();
  assert.equal(app.calls.length, 1);
  app.calls[0].resolve("translation");
  await flush();
  const result = app.requests.find(item => item.url === "/manual-result");
  events.emit("manual-recorded", { requestId: result.body.requestId, state: "locked" });
  assert.equal(app.elements.get("manual-status").textContent, "翻译完成 · 会话已锁定，未记录");
  assert.equal(app.elements.get("manual-copy").disabled, false);
});

test("clearing active manual translation cancels it without late output or recording", async () => {
  const app = harness();
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  app.context.document.getElementById("manual-input").value = "clear this request";
  app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  app.elements.get("manual-clear").onclick();
  await flush();
  assert.equal(app.calls[0].signal.aborted, true);
  assert.equal(app.elements.get("manual-output").value, "");
  assert.equal(app.elements.get("manual-status").textContent, "已清空");
  assert.equal(app.requests.filter(item => item.url === "/manual-result").length, 0);
  assert.equal(app.elements.get("manual-submit").disabled, false);
});

test("service silence lease releases models and stops manual translation", async () => {
  const app = harness();
  await flush();
  const lease = [...app.timers.values()].find(timer => timer.delay === 30000);
  lease.callback();
  assert.equal(vm.runInContext("sessions.size", app.context), 0);
  assert.equal(app.streams[0].closed, true);
  assert.equal(app.elements.get("manual-submit").disabled, true);
});

test("single model button downloads only after click and then reports installed without automatic verification", async () => {
  let created = 0;
  const translated = [];
  const app = harness({ availability: async () => "downloadable", create: async () => {
    created++;
    return { destroy() {}, translate: async text => { translated.push(text); return "verified translation"; } };
  } });
  await flush();
  assert.equal(app.elements.get("en").textContent, "未安装");
  assert.equal(app.elements.get("prepare").textContent, "点击下载微软翻译模型");
  assert.equal(created, 0);
  assert.equal(vm.runInContext("sessions.size", app.context), 0);
  app.streams[0].emit("control", { generation: 4 });
  await flush();
  await app.elements.get("prepare").onclick();
  assert.equal(created, 2);
  assert.equal(translated.length, 0);
  assert.equal(app.elements.get("en").textContent, "已安装");
  assert.equal(app.elements.get("zh").textContent, "已安装");
  assert.equal(app.elements.get("prepare").textContent, "已安装");
  assert.equal(app.elements.get("status").textContent, "已安装");
  assert.equal(app.elements.get("manual-submit").disabled, false);
});

test("download progress stays visible until both directions are installed", async () => {
  const completions = [];
  const app = harness({
    availability: async () => "downloadable",
    create: options => new Promise(resolve => {
      options.monitor({ addEventListener(name, callback) { callback({ loaded: 1, total: 2 }); } });
      completions.push(() => resolve({ destroy() {}, translate: async () => "unused" }));
    })
  });
  await flush();
  const download = app.elements.get("prepare").onclick();
  await flush();
  assert.equal(app.elements.get("prepare").textContent, "下载中...");
  assert.equal(app.elements.get("prepare").disabled, true);
  assert.equal(app.elements.get("status").textContent, "下载中...");
  assert.equal(app.elements.get("en").textContent, "下载中... 50%");
  completions[0]();
  await flush();
  assert.equal(app.elements.get("prepare").textContent, "下载中...");
  assert.equal(app.elements.get("zh").textContent, "下载中... 50%");
  completions[1]();
  await download;
  assert.equal(app.elements.get("prepare").textContent, "已安装");
  assert.equal(app.elements.get("status").textContent, "已安装");
  assert.equal(app.elements.get("prepare").disabled, false);
});

test("download without progress shows no invented percentage and accepts fractional progress", async () => {
  const completions = [];
  const progressEvents = [];
  const app = harness({
    availability: async () => "downloadable",
    create: options => new Promise(resolve => {
      options.monitor({ addEventListener(name, callback) {
        assert.equal(name, "downloadprogress");
        progressEvents.push(callback);
      } });
      completions.push(() => resolve({ destroy() {} }));
    })
  });
  await flush();
  const download = app.elements.get("prepare").onclick();
  await flush();
  assert.equal(app.elements.get("en").textContent, "下载中...");
  progressEvents[0]({ loaded: 0.25 });
  assert.equal(app.elements.get("en").textContent, "下载中... 25%");
  completions[0]();
  await flush();
  assert.equal(app.elements.get("en").textContent, "已安装");
  assert.equal(app.elements.get("zh").textContent, "下载中...");
  completions[1]();
  await download;
  assert.equal(app.elements.get("zh").textContent, "已安装");
});

test("opening during an existing download rechecks without starting another download", async () => {
  let availability = "downloading";
  let created = 0;
  const app = harness({ availability: async () => availability, create: async () => {
    created++;
    return { destroy() {}, translate: async () => "unused" };
  } });
  await flush();
  assert.equal(app.elements.get("prepare").textContent, "下载中...");
  assert.equal(created, 0);
  const recheck = [...app.timers.values()].find(timer => timer.delay === 2000);
  assert.ok(recheck);
  availability = "available";
  recheck.callback();
  await flush();
  assert.equal(created, 2);
  assert.equal(app.elements.get("prepare").textContent, "已安装");
  assert.equal(app.elements.get("status").textContent, "已安装");
});

test("one installed direction never hides another unavailable direction", async () => {
  const app = harness({ availability: async options => options.sourceLanguage === "en" ? "unavailable" : "available" });
  await flush();
  assert.equal(app.elements.get("zh").textContent, "已安装");
  assert.notEqual(app.elements.get("prepare").textContent, "已安装");
  assert.match(app.elements.get("status").textContent, /语言对不可用/);
  app.streams[0].emit("control", { generation: 4 });
  assert.notEqual(app.elements.get("prepare").textContent, "已安装");
  assert.match(app.elements.get("status").textContent, /语言对不可用/);
});

test("downloaded models are reused for real connectivity checks without duplicate preparation", async () => {
  let created = 0;
  const checked = [];
  const app = harness({ create: async () => {
    created++;
    return { destroy() {}, translate: async text => { checked.push(text); return "sample result"; } };
  } });
  await flush();
  assert.equal(app.elements.get("prepare").textContent, "已安装");
  assert.equal(app.elements.get("status").textContent, "已安装");
  assert.equal(checked.length, 0);
  await app.elements.get("prepare").onclick();
  assert.equal(created, 2);
  assert.equal(checked.length, 2);
  assert.equal(app.elements.get("status").textContent, "双向模型连通正常");
  assert.equal(app.requests.filter(item => item.url === "/manual-result" || item.url === "/result").length, 0);
});

test("connectivity failure is visible and single model action prevents duplicate checks", async () => {
  const app = harness();
  await flush();
  const check = app.elements.get("prepare").onclick();
  await flush();
  assert.equal(app.elements.get("prepare").disabled, true);
  await app.elements.get("prepare").onclick();
  assert.equal(app.calls.length, 1);
  app.calls[0].reject(Object.assign(new Error("model failed"), { name: "OperationError" }));
  await check;
  assert.equal(app.elements.get("status").textContent, "模型连通检查失败：OperationError");
  assert.equal(app.elements.get("prepare").disabled, false);
});

test("language selectors default to English-Chinese and contain 154 official candidates with pinned priorities", async () => {
  const app = harness();
  await flush();
  const codes = vm.runInContext("languageOptions.map(option => option[0])", app.context);
  assert.equal(codes.length, 154);
  assert.equal(new Set(codes).size, 154);
  assert.deepEqual(Array.from(codes.slice(0, 7)), ["en", "zh", "ja", "de", "nl", "da", "nb"]);
  assert.equal(codes[codes.length - 1], "yua");
  assert.equal(app.elements.get("manual-source").value, "en");
  assert.equal(app.elements.get("manual-target").value, "zh");
  assert.equal(app.elements.get("manual-source").innerHTML, app.elements.get("manual-target").innerHTML);
});

test("non-English manual language pairs use the selected target and release their extra model on selection changes", async () => {
  const created = [];
  const destroyed = [];
  const app = harness({ create: async options => {
    const key = `${options.sourceLanguage}>${options.targetLanguage}`;
    created.push(key);
    return { destroy() { destroyed.push(key); }, translate: async text => `${key}: ${text}` };
  } });
  await flush();
  const events = app.streams[0];
  events.emit("control", { generation: 4 });
  app.elements.get("manual-source").value = "fr";
  app.elements.get("manual-target").value = "ja";
  app.elements.get("manual-target").onchange();
  await flush();
  assert.deepEqual(created, ["en>zh", "zh>en"]);
  app.context.document.getElementById("manual-input").value = "Bonjour";
  await app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  await flush();
  const result = app.requests.find(request => request.url === "/manual-result");
  assert.equal(result.body.sourceLanguage, "fr");
  assert.equal(result.body.targetLanguage, "ja");
  assert.equal(result.body.translatedText, "fr>ja: Bonjour");
  assert.equal(vm.runInContext("sessions.size", app.context), 2);
  events.emit("manual-recorded", { requestId: result.body.requestId, state: "saved" });
  app.elements.get("manual-target").value = "de";
  app.elements.get("manual-target").onchange();
  await flush();
  assert.deepEqual(destroyed, ["fr>ja"]);
});

test("identical languages disable translation without loading another model", async () => {
  const app = harness();
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  app.elements.get("manual-target").value = "en";
  app.elements.get("manual-target").onchange();
  await flush();
  assert.equal(app.elements.get("manual-submit").disabled, true);
  assert.match(app.elements.get("manual-status").textContent, /请选择不同/);
});

test("unsupported selected language pair is not advertised as usable or downloaded", async () => {
  let created = 0;
  const app = harness({ availability: async options => options.targetLanguage === "ja" ? "unavailable" : "available", create: async () => { created++; return { destroy() {} }; } });
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  app.elements.get("manual-target").value = "ja";
  app.elements.get("manual-target").onchange();
  await flush();
  assert.equal(created, 2);
  assert.equal(app.elements.get("manual-submit").disabled, true);
  assert.match(app.elements.get("manual-status").textContent, /unavailable/);
});

test("startup, reconnect and language changes never query Windows speech pack installation", async () => {
  const app = harness();
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  await flush();
  assert.equal(app.elements.get("manual-submit").disabled, false);
  app.elements.get("manual-target").value = "ja";
  app.elements.get("manual-target").onchange();
  await flush();
  app.streams[0].emit("control", { generation: 5 });
  await flush();
  assert.equal(app.requests.some(request => request.url === "/language-resources"), false);
  assert.equal(app.elements.has("windows-languages"), false);
});

test("speech pack reminder links to official instructions without claiming installation status", () => {
  const html = fs.readFileSync(path.join(__dirname, "..", "web", "index.html"), "utf8");
  assert.match(html, /Windows 实时字幕的“字幕语言”菜单中选择语言并下载/);
  assert.match(html, /无需安装完整的 Windows 显示语言包/);
  assert.match(html, /纯文本翻译无需 Windows 语音包/);
  const link = html.match(/<a id="language-download"[^>]*>[^<]*<\/a>/)[0];
  assert.match(link, /href="https:\/\/support\.microsoft\.com\/en-us\/windows\/use-live-captions/);
  assert.doesNotMatch(link, /\bhidden\b|\bdisabled\b/);
  assert.doesNotMatch(html, /id="windows-languages"|Windows 语言包未安装|无法确认 Windows 语言包/);
});

test("clearing a pending manual model download aborts it and destroys a late session", async () => {
  let finishDownload, downloadSignal, destroyed = 0;
  const app = harness({ create: options => {
    if (options.targetLanguage !== "ja") return Promise.resolve({ destroy() {} });
    downloadSignal = options.signal;
    return new Promise(resolve => { finishDownload = () => resolve({ destroy() { destroyed++; } }); });
  } });
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  app.elements.get("manual-target").value = "ja";
  app.elements.get("manual-target").onchange();
  await flush();
  app.context.document.getElementById("manual-input").value = "cancel download";
  const download = app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  await flush();
  assert.equal(app.elements.get("manual-source").disabled, true);
  app.elements.get("manual-clear").onclick();
  assert.equal(downloadSignal.aborted, true);
  finishDownload();
  await download;
  assert.equal(destroyed, 1);
  assert.equal(app.elements.get("manual-status").textContent, "已清空");
  assert.equal(app.requests.filter(item => item.url === "/manual-result").length, 0);
});

test("clearing text while language availability is pending does not leave translation permanently disabled", async () => {
  let finishCheck;
  const app = harness({ availability: options => options.targetLanguage === "ja" ? new Promise(resolve => { finishCheck = resolve; }) : Promise.resolve("available") });
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  app.elements.get("manual-target").value = "ja";
  app.elements.get("manual-target").onchange();
  await flush();
  app.elements.get("manual-clear").onclick();
  finishCheck("available");
  await flush();
  assert.equal(app.elements.get("manual-submit").disabled, false);
});

test("manual recovery of a default caption model republishes readiness to the native caption scheduler", async () => {
  let firstEnglish = true;
  const app = harness({ create: async options => {
    if (options.sourceLanguage === "en" && firstEnglish) { firstEnglish = false; throw new Error("temporary model failure"); }
    return { destroy() {}, translate: async () => "recovered translation" };
  } });
  await flush();
  assert.equal(vm.runInContext("sessions.has('en')", app.context), false);
  app.streams[0].emit("control", { generation: 4 });
  await flush();
  app.context.document.getElementById("manual-input").value = "retry default English model";
  await app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  await flush();
  assert.equal(app.elements.get("en").textContent, "已安装");
  assert.ok(app.requests.some(request => request.url === "/status" && request.body.sourceLanguage === "en" && request.body.state === "ready" && request.body.generation === 4));
  assert.ok(app.requests.some(request => request.url === "/manual-result" && request.body.status === "ok"));
});

test("model setup contains preparation and manual download buttons without a separate check entry", () => {
  const html = fs.readFileSync(path.join(__dirname, "..", "web", "index.html"), "utf8");
  const setup = html.match(/<section class="model-setup"[\s\S]*?<\/section>/)[0];
  assert.equal((setup.match(/<button\b/g) || []).length, 2);
  for (const language of ["en", "zh"]) {
    assert.ok(setup.includes(`id="${language}"`));
    assert.equal(html.split(`id="${language}"`).length - 1, 1);
  }
  assert.doesNotMatch(html, /<progress\b/i);
  assert.equal(html.includes('id="check-models"'), false);
  assert.ok(html.indexOf('class="model-setup"') < html.indexOf('id="manual-form"'));
  assert.ok(html.includes('id="manual-source"') && html.includes('id="manual-target"'));
  assert.equal(html.includes('id="manual-direction"'), false);
  assert.match(html, /<h1>文本翻译<\/h1>[\s\S]*?id="language-download"/);
  assert.match(html, /id="language-download" href="https:\/\/support\.microsoft\.com\//);
});

test("selected non-English caption pair prepares its own translator and preserves task metadata", async () => {
  const translated = [];
  const app = harness({ create: async options => ({ destroy() {}, translate: async text => { translated.push({ ...options, text }); return "translated caption"; } }) });
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "fr", targetLanguage: "ja" });
  await flush();
  assert.ok(app.requests.some(request => request.url === "/status" && request.body.sourceLanguage === "fr" && request.body.targetLanguage === "ja" && request.body.state === "ready"));
  app.streams[0].emit("task", { ...task(1, 1), sourceLanguage: "fr", targetLanguage: "ja" });
  await flush();
  assert.equal(translated.length, 1);
  assert.equal(translated[0].sourceLanguage, "fr");
  assert.equal(translated[0].targetLanguage, "ja");
  const result = app.requests.find(request => request.url === "/result" && request.body.status === "ok");
  assert.equal(result.body.targetLanguage, "ja");
  app.streams[0].emit("control", { generation: 5, shutdown: true });
  assert.equal(vm.runInContext("captionModel", app.context), undefined);
});

test("selected caption model downloads only on click and is released on direction change", async () => {
  let created = 0, destroyed = 0;
  const app = harness({
    availability: async options => options.targetLanguage === "ja" ? "downloadable" : "available",
    create: async options => { if (options.targetLanguage === "ja") created++; return { destroy() { if (options.targetLanguage === "ja") destroyed++; } }; }
  });
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "en", targetLanguage: "ja" });
  await flush();
  assert.equal(created, 0);
  assert.match(app.elements.get("prepare").textContent, /下载所选字幕模型/);
  await app.elements.get("prepare").onclick();
  assert.equal(created, 1);
  assert.equal(app.elements.get("prepare").textContent, "已安装");
  app.streams[0].emit("control", { generation: 5, sourceLanguage: "en", targetLanguage: "zh" });
  await flush();
  assert.equal(destroyed, 1);
  assert.equal(app.elements.get("caption-model-status").hidden, true);
});

test("late selected caption model is aborted and destroyed rather than replacing a newer pair", async () => {
  let finish, signal, destroyed = 0;
  const app = harness({ create: options => options.targetLanguage === "ja" ? new Promise(resolve => {
    signal = options.signal;
    finish = () => resolve({ destroy() { destroyed++; } });
  }) : Promise.resolve({ destroy() {} }) });
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "fr", targetLanguage: "ja" });
  await flush();
  app.streams[0].emit("control", { generation: 5, sourceLanguage: "en", targetLanguage: "zh" });
  assert.equal(signal.aborted, true);
  finish();
  await flush();
  assert.equal(destroyed, 1);
  assert.equal(vm.runInContext("captionModel", app.context), undefined);
  assert.equal(app.requests.some(request => request.url === "/status" && request.body.sourceLanguage === "fr" && request.body.state === "ready"), false);
});

test("missing shared catalog stops initialization with a visible error", async () => {
  const app = harness({ catalog: { ok: false } });
  await flush();
  assert.equal(app.streams.length, 0);
  assert.equal(vm.runInContext("closed", app.context), true);
  assert.match(app.elements.get("status").textContent, /语言目录加载失败/);
});

test("unsupported selected caption pair never loads or reports a ready model", async () => {
  let created = 0;
  const app = harness({
    availability: async options => options.targetLanguage === "ja" ? "unavailable" : "available",
    create: async options => { if (options.targetLanguage === "ja") created++; return { destroy() {} }; }
  });
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "fr", targetLanguage: "ja" });
  await flush();
  await app.elements.get("prepare").onclick();
  assert.equal(created, 0);
  assert.match(app.elements.get("caption-model-status").textContent, /不支持/);
  assert.equal(app.requests.some(request => request.url === "/status" && request.body.sourceLanguage === "fr" && request.body.state === "ready"), false);
});

test("web direction adopts the initial native pair once without following later changes", async () => {
  const app = harness();
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "fr", targetLanguage: "ja" });
  await flush();
  assert.equal(app.elements.get("manual-source").value, "fr");
  assert.equal(app.elements.get("manual-target").value, "ja");
  app.streams[0].emit("control", { generation: 5, sourceLanguage: "en", targetLanguage: "zh" });
  await flush();
  assert.equal(app.elements.get("manual-source").value, "fr");
  assert.equal(app.elements.get("manual-target").value, "ja");
});

test("web swap changes only the manual pair and preserves caption direction", async () => {
  const app = harness();
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "en", targetLanguage: "zh" });
  await flush();
  app.elements.get("manual-swap").onclick();
  await flush();
  assert.equal(app.elements.get("manual-source").value, "zh");
  assert.equal(app.elements.get("manual-target").value, "en");
  assert.equal(vm.runInContext("captionSelection.sourceLanguage", app.context), "en");
  assert.equal(vm.runInContext("captionSelection.targetLanguage", app.context), "zh");
  assert.equal(app.requests.some(request => request.url.includes("direction")), false);
});

test("manual selection before the first handshake is not overwritten", async () => {
  const app = harness();
  await flush();
  app.elements.get("manual-target").value = "ja";
  app.elements.get("manual-target").onchange();
  await flush();
  app.streams[0].emit("control", { generation: 4, sourceLanguage: "zh", targetLanguage: "en" });
  await flush();
  assert.equal(app.elements.get("manual-source").value, "en");
  assert.equal(app.elements.get("manual-target").value, "ja");
});

test("web swap remains disabled during active manual translation", async () => {
  const app = harness();
  await flush();
  app.streams[0].emit("control", { generation: 4 });
  await flush();
  app.context.document.getElementById("manual-input").value = "Text";
  await app.elements.get("manual-form").onsubmit({ preventDefault() {} });
  assert.equal(app.elements.get("manual-swap").disabled, true);
  app.elements.get("manual-swap").onclick();
  assert.equal(app.elements.get("manual-source").value, "en");
  assert.equal(app.elements.get("manual-target").value, "zh");
  app.elements.get("manual-clear").onclick();
  await flush();
  assert.equal(app.elements.get("manual-swap").disabled, false);
});