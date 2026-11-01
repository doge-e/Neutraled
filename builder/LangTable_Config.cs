using System.Collections.Generic;

namespace Neutraled.Builder;

/// <summary>英文文案（C4 P1-8 / P1-5：config.json 配置键目录与语言唯一真源）。
/// 键 = ConfigSchema.cs 与 Program.cs 新增帮助行里 L("…") 的中文原文（逐字一致，含前导空格）。
/// 维护约定：ConfigSchema 新增/修改中文文案时，同步在这里加英文；缺条目时英文模式会显示中文。
/// 覆盖：--config-keys / --config-get / --config-set / --config-migrate / --lang-resolve / --lang-migrate。</summary>
public static class LangTable_Config
{
    public static void Fill(Dictionary<string, string> d)
    {
        d["布尔键只接受 true/false/1/0/on/off/yes/no，收到: {0}"] = "boolean keys accept only true/false/1/0/on/off/yes/no, got: {0}";
        d["数字键只接受整数，收到: {0}"] = "integer keys accept only whole numbers, got: {0}";
        d["===== 配置键目录（config.json）====="] = "===== config key catalog (config.json) =====";
        d["文件: {0}"] = "File: {0}";
        d["结构版本: {0}（期望 {1}）"] = "Schema version: {0} (expected {1})";
        d["规范键:"] = "Canonical keys:";
        d["owner={0}"] = "owner={0}";
        d["已废弃（只读兼容，--config-migrate 会写规范键）:"] = "Deprecated (read-only compatible; --config-migrate writes the canonical key):";
        d["提示: --config-get <键> / --config-set <键=值> / --config-migrate --dry-run"] = "Tip: --config-get <key> / --config-set <key=value> / --config-migrate --dry-run";
        d["[错误] 配置里没有这个键: {0}"] = "[error] no such config key: {0}";
        d["{0} = {1}    （未知键：不在配置键目录里，会原样保留）"] = "{0} = {1}    (unknown key: not in the config catalog; kept as-is)";
        d["旧键 {0}"] = "legacy key {0}";
        d["       查看全部键: --config-keys"] = "       see all keys: --config-keys";
        d["{0} = {1}    （类型 {2}，owner={3}，来源 {4}）"] = "{0} = {1}    (type {2}, owner={3}, source {4})";
        d["配置文件"] = "config file";
        d["默认值"] = "default";
        d["缺失"] = "missing";
        d["  注意: 该键已废弃，请改用 {0}（--config-migrate 可自动迁移）"] = "  note: this key is deprecated, use {0} instead (--config-migrate can migrate it)";
        d["[错误] 用法: --config-set <键=值>（例: --config-set auto_skip=1）"] = "[error] usage: --config-set <key=value> (e.g. --config-set auto_skip=1)";
        d["[错误] {0} 已废弃，请改用 {1}"] = "[error] {0} is deprecated, use {1} instead";
        d["[错误] {0}"] = "[error] {0}";
        d["{0} 已经是 {1}，没有改动。"] = "{0} is already {1}; nothing changed.";
        d["已写入 {0}: {1} -> {2}"] = "Wrote {0}: {1} -> {2}";
        d["（无）"] = "(none)";
        d["配置文件: {0}"] = "Config file: {0}";
        d["提示: 游戏内生效需要重启游戏；GUI 与游戏读同一个 config.json。"] = "Tip: restart the game for changes to take effect; the GUI and the game read the same config.json.";
        d["配置已是最新结构（schema_version={0}），无需迁移。"] = "Config already uses the latest schema (schema_version={0}); nothing to migrate.";
        d["===== 配置迁移预演（不写盘） ====="] = "===== config migration dry run (no writes) =====";
        d["将写入 config.json 的规范键；旧键保留（还没重新部署的旧产物仍会读）。"] = "Canonical keys that would be written to config.json; old keys are kept (older deployed outputs still read them).";
        d["  已迁移 {0} -> {1}"] = "  migrated {0} -> {1}";
        d["配置迁移完成（schema_version={0}）。旧键保留，运行时两侧都认。"] = "Config migration complete (schema_version={0}). Old keys kept; the runtime accepts both.";
        d["[语言] 生效: {0}（来源: {1}）"] = "[lang] effective: {0} (source: {1})";
        d["命令行 --lang"] = "command line --lang";
        d["config.json 的 lang"] = "config.json lang";
        d["       GUI/游戏实际显示: {0}（GUI 只支持 zh/en）"] = "       GUI/game actually shows: {0} (the GUI supports zh/en only)";
        d["       配置文件: {0}"] = "       config file: {0}";
        d["       [提示] 发现旧文件 gui_lang.txt（{0}）：已不再作为语言来源，跑 --lang-migrate 可清理。"] = "       [tip] legacy gui_lang.txt found ({0}): it is no longer a language source; run --lang-migrate to clean it up.";
        d["没有需要迁移的 gui_lang.txt（语言真源已经是 config.json 的 lang）。"] = "No gui_lang.txt to migrate (the language source is already config.json lang).";
        d["===== 语言迁移预演（不写盘） ====="] = "===== language migration dry run (no writes) =====";
        d["  gui_lang.txt = {0}"] = "  gui_lang.txt = {0}";
        d["（空）"] = "(empty)";
        d["  config.json.lang = {0}"] = "  config.json.lang = {0}";
        d["（未设置）"] = "(not set)";
        d["  将把 config.json.lang 设为 {0}，并把 gui_lang.txt 改名为 {1}"] = "  will set config.json.lang to {0} and rename gui_lang.txt to {1}";
        d["  config.json.lang 已有值（权威），只把 gui_lang.txt 改名为 {0}"] = "  config.json.lang already set (authoritative); only rename gui_lang.txt to {0}";
        d["[警告] 改名 gui_lang.txt 失败（不影响语言真源）: {0}"] = "[warning] failed to rename gui_lang.txt (language source unaffected): {0}";
        d["[语言] 迁移完成: config.json.lang = {0}（唯一真源）"] = "[lang] migration complete: config.json.lang = {0} (single source of truth)";
        d["        旧文件已归档: {0}"] = "        legacy file archived: {0}";
        d["  --lang-resolve [--json]         显示语言从哪来（--lang > NTL_LANG > config.json 的 lang > zh）"] = "  --lang-resolve [--json]         show where the language comes from (--lang > NTL_LANG > config.json lang > zh)";
        d["  --lang-migrate [--dry-run]      把旧 gui_lang.txt 并进 config.json 的 lang 并归档（语言唯一真源）"] = "  --lang-migrate [--dry-run]      merge legacy gui_lang.txt into config.json lang and archive it (single language source)";
        d["  配置键（config.json 的单一事实源）:"] = "  Config keys (single source of truth for config.json):";
        d["  --config-keys [--json]          列出全部键（类型 / owner / 默认值 / 是否废弃）"] = "  --config-keys [--json]          list every key (type / owner / default / deprecated)";
        d["  --config-get <键> [--json]      查看一个键的生效值与来源"] = "  --config-get <key> [--json]     show one key's effective value and source";
        d["  --config-set <键=值> [--json]   读-改-写 config.json（未知键与注释原样保留）"] = "  --config-set <key=value> [--json]   read-modify-write config.json (unknown keys and comments preserved)";
        d["  --config-migrate [--dry-run] [--json]   把废弃键迁移成规范键（旧键保留兼容）"] = "  --config-migrate [--dry-run] [--json]   migrate deprecated keys to canonical ones (old keys kept)";
    }
}
