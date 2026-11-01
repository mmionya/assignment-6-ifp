#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
mkdir -p "runtimes/osx-$(uname -m)/native"
cc -std=c11 -O2 -Wall -Wextra -Werror -dynamiclib astra_native.c -o "runtimes/osx-$(uname -m)/native/libastra_native.dylib"
