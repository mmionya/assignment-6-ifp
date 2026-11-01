#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
mkdir -p runtimes/linux-x64/native
gcc -std=c11 -O2 -Wall -Wextra -Werror -fPIC -shared astra_native.c -o runtimes/linux-x64/native/libastra_native.so
