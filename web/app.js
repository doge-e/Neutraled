/* Neutraled 本地网页界面（零依赖：原生 fetch + DOM，无框架、无 CDN）。
   设计口径：
     · 只有本机可访问（服务端已经拒了非回环请求），所以这里不做登录/鉴权；
     · 破坏性操作（切换配置档 / 回切快照 / 恢复恢复点 / 执行队列 / 安装 mod / 部署 / 关闭服务）一律 window.confirm 二次确认；
     · 主题：/api/themes 的活动主题里若有 colors 字典，逐键写进 :root 的 CSS 变量，样式表里的 var() 都带兜底值；
     · 所有从服务端来的字符串都走 textContent（不用 innerHTML），mod 名/插件名里的尖括号不会变成 HTML。 */
'use strict';

const TABS = [
  { id: 'overview',  label: '总览' },
  { id: 'mods',      label: 'mod 管理' },
  { id: 'profiles',  label: '配置档' },
  { id: 'snapshots', label: '快照' },
  { id: 'restore',   label: '恢复点' },
  { id: 'downloads', label: '下载' },
  { id: 'plugins',   label: '插件' },
  { id: 'themes',    label: '主题' },
  { id: 'lang',      label: '语言' },
  { id: 'deploy',    label: '部署' }
];

const CHAPTERS = ['root', 'chapter1', 'chapter2', 'chapter3', 'chapter4'];

const state = { status: null, tab: 'overview', themes: null, snapMod: '' };

/* ------------------------------------------------------------------ 基础设施 */

function el(tag, attrs, ...kids) {
  const n = document.createElement(tag);
  if (attrs) {
    for (const k of Object.keys(attrs)) {
      const v = attrs[k];
      if (v === null || v === undefined || v === false) continue;
      if (k === 'class') n.className = v;
      else if (k === 'text') n.textContent = v;
      else if (k.startsWith('on') && typeof v === 'function') n.addEventListener(k.slice(2), v);
      else n.setAttribute(k, v === true ? '' : String(v));
    }
  }
  for (const kid of kids) {
    if (kid === null || kid === undefined || kid === false) continue;
    if (Array.isArray(kid)) { for (const k2 of kid) if (k2 !== null && k2 !== undefined) n.append(k2 instanceof Node ? k2 : document.createTextNode(String(k2))); }
    else n.append(kid instanceof Node ? kid : document.createTextNode(String(kid)));
  }
  return n;
}

async function api(path, method, body) {
  const opt = { method: method || 'GET', headers: {} };
  if (body !== undefined && body !== null) {
    opt.headers['Content-Type'] = 'application/json';
    opt.body = JSON.stringify(body);
  }
  const res = await fetch(path, opt);
  let data = null;
  try { data = await res.json(); } catch (e) { data = null; }
  if (!res.ok) {
    const msg = (data && data.error) ? data.error : ('HTTP ' + res.status);
    throw new Error(msg);
  }
  return data;
}

function asList(data, ...keys) {
  if (Array.isArray(data)) return data;
  for (const k of keys) if (data && Array.isArray(data[k])) return data[k];
  return [];
}

let toastTimer = null;
function toast(msg, kind) {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.className = 'toast' + (kind ? ' ' + kind : '');
  t.hidden = false;
  if (toastTimer) clearTimeout(toastTimer);
  toastTimer = setTimeout(function () { t.hidden = true; }, 4200);
}

async function guard(fn) {
  document.body.classList.add('busy');
  try { await fn(); }
  catch (e) { toast('失败：' + e.message, 'bad'); }
  finally { document.body.classList.remove('busy'); }
}

function fmtBytes(n) {
  n = Number(n) || 0;
  if (n < 1024) return n + ' B';
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
  if (n < 1024 * 1024 * 1024) return (n / 1024 / 1024).toFixed(1) + ' MB';
  return (n / 1024 / 1024 / 1024).toFixed(2) + ' GB';
}

function table(headers, rows) {
  if (!rows.length) return el('p', { class: 'empty', text: '（空）' });
  return el('div', { class: 'table-wrap' },
    el('table', null,
      el('thead', null, el('tr', null, headers.map(function (h) { return el('th', { text: h }); }))),
      el('tbody', null, rows)));
}

/* ------------------------------------------------------------------ 主题 */

function applyTheme(theme) {
  const root = document.documentElement;
  const known = ['bg', 'panel', 'panel-2', 'panel2', 'fg', 'muted', 'border', 'accent', 'accent-fg', 'danger', 'ok', 'warn', 'dim', 'error'];
  for (const k of known) root.style.removeProperty('--' + k);
  root.style.removeProperty('--font-family');
  root.style.removeProperty('--font-size');
  if (!theme) return;
  const colors = theme.colors || {};
  // 主题里的颜色键名（SPEC §3.6：bg/fg/accent/dim/error/warn/ok/highlight/selection…）与界面变量名不完全同名，
  // 这里做一层映射，保证 dim→--muted、error→--danger 这类配色真的生效（实测：只写 --dim 时界面看不出变化）。
  const alias = { dim: '--muted', error: '--danger', panel2: '--panel-2', 'panel-2': '--panel-2' };
  for (const k of Object.keys(colors)) {
    const v = colors[k];
    if (typeof v === 'string' && v) {
      root.style.setProperty('--' + k, v);
      if (alias[k]) root.style.setProperty(alias[k], v);
    }
  }
  if (theme.fontFamily) root.style.setProperty('--font-family', theme.fontFamily);
  if (theme.fontSize) root.style.setProperty('--font-size', (Number(theme.fontSize) || 14) + 'px');
}

async function loadThemes() {
  const data = await api('/api/themes');
  state.themes = data || {};
  const list = asList(data, 'themes');
  const active = (data && data.active) || '';
  const hit = list.filter(function (t) { return t.id === active; })[0];
  applyTheme(hit || null);
}

/* ------------------------------------------------------------------ 顶部 / 页脚 */

function paintMeta(s) {
  state.status = s;
  document.getElementById('meta-platform').textContent = '平台: ' + (s.platform || '—');
  const rootEl = document.getElementById('meta-root');
  rootEl.textContent = '游戏根: ' + (s.gameRoot || '—');
  rootEl.title = s.gameRoot || '';
  document.getElementById('meta-lang').textContent = '语言: ' + (s.lang || 'zh');
  document.getElementById('foot-version').textContent = '版本 ' + (s.version || '—');
  document.getElementById('foot-port').textContent = s.port ? ('端口 ' + s.port) : '';
}

/* ------------------------------------------------------------------ 各面板 */

async function renderOverview(view) {
  const s = await api('/api/status');
  paintMeta(s);
  const kv = function (k, v) {
    return el('div', { class: 'kv' }, el('span', { class: 'k', text: k }), el('span', { class: 'v', text: v }));
  };
  view.append(
    el('section', { class: 'panel' },
      el('h2', { text: '总览' }),
      el('div', { class: 'grid' },
        kv('游戏根', s.gameRoot || '—'),
        kv('平台', s.platform || '—'),
        kv('章节后缀', s.chapterSuffix || '—'),
        kv('mod 总数', String(s.mods == null ? 0 : s.mods)),
        kv('已启用 mod', String(s.enabled == null ? 0 : s.enabled)),
        kv('当前配置档', s.active_profile || '（未使用）'),
        kv('界面语言', s.lang || 'zh'),
        kv('当前主题', s.theme || 'default'),
        kv('版本', s.version || '—'),
        kv('配置档', asList(s.profiles).join('、') || '（无）'))),
    el('section', { class: 'panel' },
      el('h2', { text: '快捷操作' }),
      el('p', { class: 'muted', text: '网页界面与命令行走同一套模块，改完立即落盘；部署会调用主程序的部署流程。' }),
      el('div', { class: 'row' },
        el('button', { text: '刷新', onclick: function () { return load(); } }),
        el('button', { text: '部署 root', onclick: function () { return doDeploy('root'); } }),
        el('button', { text: '部署当前章节', onclick: function () { return doDeploy(currentChapter()); } }))));
}

function currentChapter() {
  const s = state.status || {};
  const suf = String(s.chapterSuffix || '');
  const m = suf.match(/chapter\d/);
  return m ? m[0] : 'root';
}

async function renderMods(view) {
  const data = await api('/api/mods');
  const list = asList(data, 'mods');
  const rows = list.map(function (m) {
    const on = m.enabled !== false;
    return el('tr', null,
      el('td', { class: 'mono', text: m.id || '' }),
      el('td', { text: m.name || '' }),
      el('td', { class: 'mono', text: m.version || '' }),
      el('td', { text: m.author || '' }),
      el('td', { text: m.chapter || '' }),
      el('td', null, el('span', { class: 'badge ' + (on ? 'on' : 'off'), text: on ? '已启用' : '已禁用' })),
      el('td', null, el('button', {
        text: on ? '禁用' : '启用',
        onclick: function () { return toggleMod(m.id, !on); }
      })));
  });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: 'mod 管理（' + list.length + '）' }),
    el('p', { class: 'muted', text: '启用/禁用会直接改写 mod 的清单与目录名（与命令行的 mods on|off 同一条路径），禁用后游戏不再加载。' }),
    table(['id', '名称', '版本', '作者', '章节', '状态', '操作'], rows)));
}

async function toggleMod(id, want) {
  await guard(async function () {
    await api('/api/mods/toggle', 'POST', { id: id, enabled: want });
    toast((want ? '已启用 ' : '已禁用 ') + id, 'good');
    await load();
  });
}

async function renderProfiles(view) {
  const list = asList(await api('/api/profiles'), 'profiles');
  const rows = list.map(function (p) {
    const ids = Array.isArray(p.enabled) ? p.enabled : [];
    return el('tr', null,
      el('td', { class: 'mono', text: p.id || '' }),
      el('td', { text: p.name || '' }),
      el('td', { class: 'muted', text: p.description || '' }),
      el('td', { class: 'mono', text: ids.length ? ids.join(', ') : '（空）' }),
      el('td', null, p.active ? el('span', { class: 'badge on', text: '使用中' }) : el('span', { class: 'badge', text: '—' })),
      el('td', null,
        el('button', { text: '套用', onclick: function () { return useProfile(p.id); } }),
        ' ',
        el('button', { text: '保存当前', onclick: function () { return saveProfile(p.id, p.name); } })));
  });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '配置档（' + list.length + '）' }),
    el('p', { class: 'muted', text: '套用配置档会把 mod 启用状态整体切换成该档记录的样子；保存当前会把现在的启用状态写回该档。' }),
    table(['id', '名称', '说明', '启用的 mod', '状态', '操作'], rows)),
    el('section', { class: 'panel' },
      el('h2', { text: '新建配置档' }),
      el('div', { class: 'row' },
        el('input', { type: 'text', id: 'new-profile-id', placeholder: 'id（字母数字-_）' }),
        el('input', { type: 'text', id: 'new-profile-name', placeholder: '名称（可留空）' }),
        el('button', { class: 'primary', text: '从当前状态创建', onclick: createProfile }))));
}

async function useProfile(id) {
  if (!window.confirm('确认套用配置档「' + id + '」？\n当前的 mod 启用状态会被改写。')) return;
  await guard(async function () {
    const r = await api('/api/profile/use', 'POST', { id: id });
    toast('已套用「' + id + '」，改动 ' + (r && r.changed != null ? r.changed : '?') + ' 项', 'good');
    await load();
  });
}

async function saveProfile(id, name) {
  if (!window.confirm('把当前的 mod 启用状态保存进配置档「' + (name || id) + '」？\n该档原有记录会被覆盖。')) return;
  await guard(async function () {
    const r = await api('/api/profile/save', 'POST', { id: id, name: name || id });
    toast('已保存「' + id + '」，记录 ' + (r && r.captured != null ? r.captured : '?') + ' 个 mod', 'good');
    await load();
  });
}

async function createProfile() {
  const id = (document.getElementById('new-profile-id').value || '').trim();
  const name = (document.getElementById('new-profile-name').value || '').trim();
  if (!id) { toast('请先填 id', 'bad'); return; }
  await guard(async function () {
    const r = await api('/api/profile/save', 'POST', { id: id, name: name || id });
    toast('已创建「' + id + '」，记录 ' + (r && r.captured != null ? r.captured : '?') + ' 个 mod', 'good');
    await load();
  });
}

async function renderSnapshots(view) {
  const mods = asList(await api('/api/mods'), 'mods');
  if (!state.snapMod && mods.length) state.snapMod = mods[0].id;
  const picker = el('select', { onchange: function (e) { state.snapMod = e.target.value; return load(); } });
  picker.append(el('option', { value: '', text: '（全部 mod）' }));
  for (const m of mods) picker.append(el('option', { value: m.id, text: m.id + ' — ' + (m.name || ''), selected: m.id === state.snapMod }));
  picker.value = state.snapMod;
  const q = state.snapMod ? ('?modId=' + encodeURIComponent(state.snapMod)) : '';
  const list = asList(await api('/api/snapshots' + q), 'snapshots');
  const rows = list.map(function (s) {
    return el('tr', null,
      el('td', { class: 'mono', text: s.modId || '' }),
      el('td', { class: 'mono', text: s.version || '' }),
      el('td', { text: s.name || '' }),
      el('td', { text: s.author || '' }),
      el('td', { class: 'muted', text: s.created || '' }),
      el('td', { text: s.source || '' }),
      el('td', { class: 'mono', text: (s.files || 0) + ' / ' + fmtBytes(s.bytes) }),
      el('td', null, el('button', { text: '回切到此版本', onclick: function () { return useSnapshot(s.modId, s.version); } })));
  });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '快照（' + list.length + '）' }),
    el('p', { class: 'muted', text: '快照是 mod 的旧版本备份；回切会用快照覆盖 mods/ 下的对应目录，属于破坏性操作。' }),
    el('div', { class: 'row' }, el('span', { class: 'muted', text: '筛选 mod：' }), picker),
    table(['modId', '版本', '名称', '作者', '创建时间', '来源', '文件/体积', '操作'], rows)));
}

async function useSnapshot(modId, version) {
  if (!window.confirm('确认把 mod「' + modId + '」回切到版本 ' + version + '？\n现有文件会被快照覆盖。')) return;
  await guard(async function () {
    const r = await api('/api/snapshot/use', 'POST', { modId: modId, version: version });
    toast('已回切 ' + modId + ' → ' + version + '，恢复 ' + (r && r.restored != null ? r.restored : '?') + ' 个文件', 'good');
    await load();
  });
}

async function renderRestore(view) {
  const list = asList(await api('/api/restore'), 'points');
  const rows = list.map(function (p) {
    return el('tr', null,
      el('td', { class: 'mono', text: p.id || '' }),
      el('td', { text: p.name || '' }),
      el('td', { class: 'muted', text: p.created || '' }),
      el('td', { text: p.from || '' }),
      el('td', { class: 'mono', text: p.profileId || '' }),
      el('td', { class: 'mono', text: fmtBytes(p.bytes) }),
      el('td', null, p.hasData ? el('span', { class: 'badge on', text: '含数据' }) : el('span', { class: 'badge', text: '仅清单' })),
      el('td', null, el('button', { class: 'danger', text: '恢复', onclick: function () { return applyRestore(p.id, p.hasData); } })));
  });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '恢复点（' + list.length + '）' }),
    el('p', { class: 'muted', text: '恢复点记录某一时刻的完整状态（含游戏数据备份）；恢复会覆盖 dist/ 与游戏数据，无法自动撤销。' }),
    table(['id', '名称', '创建时间', '来源', '配置档', '体积', '数据', '操作'], rows)));
}

async function applyRestore(id, hasData) {
  const extra = hasData ? '\n这会覆盖 dist/ 与游戏数据文件。' : '\n该恢复点不含数据备份，只会还原清单。';
  if (!window.confirm('确认恢复恢复点「' + id + '」？' + extra)) return;
  await guard(async function () {
    const r = await api('/api/restore/apply', 'POST', { id: id });
    toast('已恢复「' + id + '」，' + (r && r.applied != null ? r.applied : '?') + ' 项', 'good');
    await load();
  });
}

async function renderDownloads(view) {
  const box = el('input', { type: 'search', id: 'gb-q', placeholder: '搜索 GameBanana（如：Chapter 4 汉化）', style: 'min-width:320px' });
  const installChapter = el('select', { id: 'gb-chapter' });
  for (const c of CHAPTERS) installChapter.append(el('option', { value: c, text: c }));
  installChapter.value = currentChapter();

  const resultBox = el('div', { id: 'gb-results', class: 'muted', text: '输入关键词后回车搜索。' });
  const searchRow = el('div', { class: 'row' },
    box,
    el('button', { class: 'primary', text: '搜索', onclick: doSearch }),
    el('span', { class: 'muted', text: '安装章节：' }), installChapter);
  box.addEventListener('keydown', function (e) { if (e.key === 'Enter') doSearch(); });

  const queue = asList(await api('/api/queue'), 'queue');
  const qrows = queue.map(function (it) {
    const st = String(it.state || '');
    const bad = /fail|error|失败/i.test(st);
    return el('tr', null,
      el('td', { class: 'mono', text: it.id || '' }),
      el('td', { text: it.modName || String(it.modId || '') }),
      el('td', { class: 'mono', text: it.fileName || '' }),
      el('td', null, el('span', { class: 'badge' + (bad ? ' bad' : ''), text: st || '—' })),
      el('td', { class: 'mono', text: (it.got && it.size) ? (fmtBytes(it.got) + ' / ' + fmtBytes(it.size)) : '' }),
      el('td', { class: 'muted', text: it.error || '' }));
  });

  view.append(
    el('section', { class: 'panel' },
      el('h2', { text: 'GameBanana 搜索' }),
      el('p', { class: 'muted', text: '搜索走仓库既有的 GameBanana 客户端，结果已按黑名单过滤。' }),
      searchRow, resultBox),
    el('section', { class: 'panel' },
      el('h2', { text: '下载队列（' + queue.length + '）' }),
      el('div', { class: 'row' },
        el('button', { class: 'primary', text: '执行队列（下载并安装）', onclick: function () { return runQueue(true); } }),
        el('button', { text: '执行队列（仅下载）', onclick: function () { return runQueue(false); } })),
      table(['id', 'mod', '文件', '状态', '进度', '错误'], qrows)));
}

async function doSearch() {
  const q = (document.getElementById('gb-q').value || '').trim();
  const box = document.getElementById('gb-results');
  if (!q) { toast('请输入搜索关键词', 'bad'); return; }
  await guard(async function () {
    box.textContent = '搜索中…';
    const data = await api('/api/gb/search?q=' + encodeURIComponent(q));
    const list = asList(data, 'results');
    box.textContent = '';
    if (data && data.filtered) box.append(el('p', { class: 'muted', text: '黑名单过滤掉 ' + data.filtered + ' 条。' }));
    if (!list.length) { box.append(el('p', { class: 'empty', text: '没有结果。' })); return; }
    const rows = list.map(function (r) {
      return el('tr', null,
        el('td', { class: 'mono', text: String(r.id == null ? '' : r.id) }),
        el('td', { text: r.name || '' }),
        el('td', { text: r.author || '' }),
        el('td', { class: 'mono', text: String(r.downloadCount == null ? '' : r.downloadCount) }),
        el('td', { class: 'muted', text: (r.description || '').slice(0, 160) }),
        el('td', null, el('button', { class: 'primary', text: '安装', onclick: function () { return installMod(r.id, r.name); } })));
    });
    box.append(table(['id', '名称', '作者', '下载量', '简介', '操作'], rows));
  });
}

async function installMod(modId, name) {
  const chapter = (document.getElementById('gb-chapter') || {}).value || 'root';
  if (!window.confirm('确认安装「' + (name || modId) + '」到 ' + chapter + '？\n安装过程会联网下载并写入 mods/ 目录。')) return;
  await guard(async function () {
    const r = await api('/api/gb/install', 'POST', { modId: modId, chapter: chapter });
    toast((r && r.ok ? '安装完成' : '安装结束（返回码 ' + (r && r.code) + '）') + '：' + (name || modId), r && r.ok ? 'good' : '');
    await load();
  });
}

async function runQueue(autoInstall) {
  const msg = autoInstall
    ? '确认执行下载队列？\n会依次下载并自动安装，直接写入 mods/ 目录。'
    : '确认执行下载队列（仅下载）？';
  if (!window.confirm(msg)) return;
  await guard(async function () {
    const r = await api('/api/queue/run', 'POST', { autoInstall: autoInstall });
    toast('队列执行完成，处理 ' + (r && r.processed != null ? r.processed : '?') + ' 项', 'good');
    await load();
  });
}

async function renderPlugins(view) {
  const list = asList(await api('/api/plugins'), 'plugins');
  const rows = list.map(function (p) {
    const st = String(p.state || '');
    const bad = /error|fail|invalid|错误/i.test(st);
    const diags = Array.isArray(p.diagnostics) ? p.diagnostics : [];
    return el('tr', null,
      el('td', { class: 'mono', text: p.id || '' }),
      el('td', { text: p.name || '' }),
      el('td', { class: 'mono', text: p.version || '' }),
      el('td', { text: p.author || '' }),
      el('td', null, el('span', { class: 'badge' + (bad ? ' bad' : ''), text: st || '—' })),
      el('td', { class: 'muted', text: (p.description || '') + (diags.length ? ('\n诊断：' + diags.join('；')) : '') }));
  });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '插件（' + list.length + '）' }),
    el('p', { class: 'muted', text: '插件目录：Neutraled/plugins/<id>/plugin.json；这里只读展示，安装/卸载请用命令行。' }),
    table(['id', '名称', '版本', '作者', '状态', '说明'], rows)));
}

async function renderThemes(view) {
  const data = await api('/api/themes');
  state.themes = data || {};
  const list = asList(data, 'themes');
  const active = (data && data.active) || '';
  const rows = list.map(function (t) {
    const colors = t.colors || {};
    const swatch = Object.keys(colors).slice(0, 8).map(function (k) {
      const v = String(colors[k] || '');
      return el('span', { title: k + '=' + v, style: 'display:inline-block;width:14px;height:14px;border-radius:3px;border:1px solid var(--border);background:' + v });
    });
    return el('tr', null,
      el('td', { class: 'mono', text: t.id || '' }),
      el('td', { text: t.name || '' }),
      el('td', { text: t.author || '' }),
      el('td', { class: 'muted', text: t.description || '' }),
      el('td', null, swatch),
      el('td', null, (t.id === active)
        ? el('span', { class: 'badge on', text: '使用中' })
        : el('button', { text: '应用', onclick: function () { return useTheme(t.id); } })));
  });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '主题（' + list.length + '）' }),
    el('p', { class: 'muted', text: '主题同时影响命令行与网页界面：颜色取自主题的 colors 字典，缺项自动回退到内置配色。' }),
    table(['id', '名称', '作者', '说明', '配色', '操作'], rows)));
}

async function useTheme(id) {
  await guard(async function () {
    const r = await api('/api/theme/use', 'POST', { id: id });
    if (r && r.ok === false) { toast('主题切换失败（返回码 ' + r.code + '）', 'bad'); return; }
    await loadThemes();
    await load();
    toast('已应用主题「' + id + '」', 'good');
  });
}

async function renderLang(view) {
  const data = await api('/api/lang');
  const active = (data && data.active) || 'zh';
  const avail = asList(data, 'available');
  const builtIn = asList(data, 'builtIn');
  const picker = el('select', { id: 'lang-code' });
  const codes = avail.slice();
  for (const c of builtIn) if (codes.indexOf(c) < 0) codes.push(c);
  if (codes.indexOf(active) < 0) codes.push(active);
  for (const c of codes) picker.append(el('option', { value: c, text: c + (builtIn.indexOf(c) >= 0 ? '（内置）' : ''), selected: c === active }));
  picker.value = active;
  const report = el('div', { id: 'lang-report' });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '界面语言' }),
    el('p', { class: 'muted', text: '语言选择会写进 config.json 的 lang 键，并加载 Neutraled/lang/<code>.json 外部语言包；命令行的 --lang / NTL_LANG 优先级更高。' }),
    el('div', { class: 'row' },
      el('span', { class: 'muted', text: '当前：' + active }),
      picker,
      el('button', { class: 'primary', text: '切换', onclick: useLang }),
      el('button', { text: '查看译文覆盖率', onclick: checkCoverage })),
    report,
    el('h3', { text: '可用语言' }),
    el('p', { class: 'mono', text: codes.join('、') || '（无）' })));
}

async function useLang() {
  const code = (document.getElementById('lang-code') || {}).value;
  if (!code) return;
  await guard(async function () {
    await api('/api/lang/use', 'POST', { code: code });
    await load();
    toast('已切换到语言「' + code + '」（控制台输出同步生效）', 'good');
  });
}

async function checkCoverage() {
  const code = (document.getElementById('lang-code') || {}).value || 'zh';
  await guard(async function () {
    const r = await api('/api/lang/coverage?code=' + encodeURIComponent(code));
    const box = document.getElementById('lang-report');
    box.textContent = '';
    box.append(
      el('h3', { text: '覆盖率（' + code + '，返回码 ' + (r && r.result) + '）' }),
      el('pre', { class: 'report', text: (r && r.report) || '（无输出）' }));
  });
}

async function renderDeploy(view) {
  const chapter = el('select', { id: 'deploy-chapter' });
  for (const c of CHAPTERS) chapter.append(el('option', { value: c, text: c }));
  chapter.value = currentChapter();
  const out = el('div', { id: 'deploy-out' });
  view.append(el('section', { class: 'panel' },
    el('h2', { text: '部署' }),
    el('p', { class: 'muted', text: '部署会把 mods/ 的改动应用到 dist/ 并同步游戏数据目录（与命令行的 deploy 同一条流程）。若主程序未注入部署钩子，这里会返回 501。' }),
    el('div', { class: 'row' },
      el('span', { class: 'muted', text: '章节：' }),
      chapter,
      el('button', { class: 'primary', text: '开始部署', onclick: function () { return doDeploy(chapter.value); } })),
    out));
}

async function doDeploy(chapter) {
  if (!window.confirm('确认部署到 ' + chapter + '？\n会覆盖 dist/ 与游戏数据目录，属于破坏性操作。')) return;
  await guard(async function () {
    const r = await api('/api/deploy', 'POST', { chapter: chapter });
    const box = document.getElementById('deploy-out');
    if (box) {
      box.textContent = '';
      box.append(el('p', { class: r && r.ok ? 'ok' : 'err', text: (r && r.ok ? '部署成功：' : '部署结束（返回码 ' + (r && r.code) + '）：') + chapter }));
    }
    toast(r && r.ok ? ('部署完成：' + chapter) : ('部署返回码 ' + (r && r.code)), r && r.ok ? 'good' : 'bad');
    if (state.tab === 'overview') await load();
  });
}

/* ------------------------------------------------------------------ 调度 */

const RENDER = {
  overview: renderOverview,
  mods: renderMods,
  profiles: renderProfiles,
  snapshots: renderSnapshots,
  restore: renderRestore,
  downloads: renderDownloads,
  plugins: renderPlugins,
  themes: renderThemes,
  lang: renderLang,
  deploy: renderDeploy
};

async function load() {
  const view = document.getElementById('view');
  view.textContent = '';
  view.append(el('p', { class: 'muted', text: '加载中…' }));
  const fn = RENDER[state.tab] || renderOverview;
  try {
    view.textContent = '';
    await fn(view);
  } catch (e) {
    view.textContent = '';
    view.append(el('div', { class: 'error', text: '加载失败：' + e.message }));
  }
  document.querySelectorAll('#tabs button').forEach(function (b) {
    b.classList.toggle('active', b.dataset.tab === state.tab);
  });
}

function buildTabs() {
  const tabs = document.getElementById('tabs');
  for (const t of TABS) {
    tabs.append(el('button', {
      text: t.label,
      'data-tab': t.id,
      onclick: function () { state.tab = t.id; return load(); }
    }));
  }
}

async function refreshStatus() {
  try { paintMeta(await api('/api/status')); } catch (e) { /* 状态拉不到就先不管，面板自己会报错 */ }
}

function boot() {
  buildTabs();
  document.getElementById('btn-refresh').addEventListener('click', function () { return load(); });
  document.getElementById('btn-shutdown').addEventListener('click', shutdown);
  loadThemes().catch(function () { /* 主题拿不到就用内置配色 */ });
  load();
  setInterval(refreshStatus, 15000);
}

async function shutdown() {
  if (!window.confirm('确认关闭本地网页服务？\n关闭后需要重新运行 --web 才能再打开。')) return;
  try {
    await api('/api/shutdown', 'POST', {});
    document.body.innerHTML = '<main class="view"><section class="panel"><h2>服务已关闭</h2>'
      + '<p class="muted">网页界面已退出，这个页面可以关掉了。重新启动：在命令行运行 --web。</p></section></main>';
  } catch (e) {
    toast('关闭失败：' + e.message, 'bad');
  }
}

document.addEventListener('DOMContentLoaded', boot);
