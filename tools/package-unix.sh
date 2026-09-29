#!/bin/sh
# Neutraled 跨平台打包入口（Linux / macOS）
#
#   ⚠ 未实机验证：本文件在 Windows 上编写，尚未在 Linux/macOS 上真跑过。
#
# 真正的打包器是 release-pack/make-release.mjs（零依赖 Node 脚本，Node 18+）；
# 本文件只是薄封装：定位打包器、给跨平台的默认值、把参数原样转发。
#
# 用法：
#   tools/package-unix.sh                    # 默认 linux-x64 / macOS 自动选 rid，出 installer 包
#   tools/package-unix.sh --version 1.0.0
#   tools/package-unix.sh --layout full      # 内嵌源码的完整包（离线安装用）
#   NTL_PACKAGER=/path/to/make-release.mjs tools/package-unix.sh
#
# 打包器位置（按顺序查找）：
#   1) $NTL_PACKAGER
#   2) <repo>/release-pack/make-release.mjs          （把 release-pack 收进仓库时）
#   3) <repo>/tools/release-pack/make-release.mjs
#   4) release-pack 不在此克隆里时：设 NTL_PACKAGER 指过去
set -eu

HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT=$(CDPATH= cd -- "$HERE/.." && pwd)

PKG=""
if [ -n "${NTL_PACKAGER:-}" ]; then
  if [ ! -f "$NTL_PACKAGER" ]; then
    echo "NTL_PACKAGER 指向的文件不存在：$NTL_PACKAGER" >&2
    exit 1
  fi
  PKG="$NTL_PACKAGER"
else
  for c in "$ROOT/release-pack/make-release.mjs" "$HERE/release-pack/make-release.mjs" "$ROOT/tools/release-pack/make-release.mjs"; do
    if [ -f "$c" ]; then PKG="$c"; break; fi
  done
fi
if [ -z "$PKG" ]; then
  echo "找不到 make-release.mjs。请把它所在目录告诉本脚本：" >&2
  echo "  NTL_PACKAGER=/path/to/release-pack/make-release.mjs $0 \"$@\"" >&2
  echo "（release-pack/ 目前不在仓库里；要么把它收进 <repo>/release-pack/，要么用上面的环境变量）" >&2
  exit 1
fi

if ! command -v node >/dev/null 2>&1; then
  echo "找不到 node（打包器是零依赖 Node 脚本，装 Node 18+ 即可）" >&2
  exit 1
fi

# 未指定 --rid 时按本机推断
RID=""
case "$(uname -s)" in
  Linux)  case "$(uname -m)" in aarch64|arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac ;;
  Darwin) case "$(uname -m)" in arm64) RID=osx-arm64 ;; *) RID=osx-x64 ;; esac ;;
esac

has_arg() { for a in "$@"; do [ "$a" = "$1" ] && return 0; done; return 1; }

ARGS=""
if [ -n "$RID" ] && ! has_arg --rid "$@"; then ARGS="$ARGS --rid $RID"; fi
if ! has_arg --out "$@" && [ -n "${NTL_OUT:-}" ]; then ARGS="$ARGS --out $NTL_OUT"; fi

echo "打包器 : $PKG"
echo "仓库   : $ROOT"
[ -n "$RID" ] && echo "RID    : $RID"

# shellcheck disable=SC2086
exec node "$PKG" --project "$ROOT" $ARGS "$@"
