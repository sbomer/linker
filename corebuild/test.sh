#!/usr/bin/env bash

# test.sh will bootstrap the cli and ultimately call "dotnet test".
# If no configuration is specified, the default configuration will be
# set to netcore_Debug (see config.json).

working_tree_root="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
$working_tree_root/run.sh test -Project=../test/Mono.Linker.Tests/Mono.Linker.Tests.csproj $@
exit $?

