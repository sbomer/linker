The Mono linker is a tool one can use to only ship the minimal
possible set of functions that a set of programs might require to run
as opposed to the full libraries.

It is used by the various Xamarin products to extract only the bits of
code that are needed to run an application on Android, iOS and other
platforms.

This file was extracted from Mono (github.com/mono/mono) on November
1st, 2016 to allow easier sharing of the linker code with other .NET
projects.


# Building the linker

## Building using a script

To build the linker, run

```
.\restore.cmd
.\build.cmd
```

from the root of the repository. Use the corresponding .sh scripts to
build on linux. The first time any of these scripts is run, it will
get a specific version of the dotnet cli (specified in `.cliversion`)
and use this to restore and build the linker and the linker tests. The
arguments `-Release` or `-Debug` can be used as arguments to
`.\build.cmd` to specify the configuration.

## Building manually

You can also build the linker manually as follows. Make sure to use a
version of dotnet that supports the msbuild-based `.csproj` files (you
need at least preview4), or just use the restored cli in
`Tools\dotnetcli\dotnet.exe` after running `.\build.cmd` once.

```
# build linker
dotnet build

# build tests
cd test\Mono.Linker.Tests
dotnet restore
dotnet build
cd ..\..
```

# Running tests

## Running tests using a script

To run the linker tests, run

```
.\test.cmd
```

## Running tests manually

You can also run the tests manually using `dotnet` (see the build
instructions for notes about the dotnet cli version). This allows more
flexibility in specifying which tests to run.

Tests should be run from the `Mono.Linker.Tests` directory:

```
cd test\Mono.Linker.Tests
```

To run all tests:

```
dotnet test -f net451
```

The unit tests set up a linker pipeline and execute it on some simple
assemblies and check that the output assemblies are linked
correctly. To run just the unit tests:

```
dotnet test -f net451 --testCaseFilter:"FullyQualifiedName~UnitTests"
```

The integration tests build, link, and run some simple projects using
the dotnet cli linker integration. To run just the integration tests:

```
dotnet test -f net451 --testCaseFilter:"FullyQualifiedName~IntegrationTests"
```
