# Size analysis tooling

Developers using trimming and/or NativeAot are often interested in minimizing the size of their applications. For this it is useful to understand the size breakdown of the app (what is taking up space on disk), and understanding the dependency relations that caused data to be preserved in the output. We have heard from developers that these kinds of questions are difficult to answer with the limited tooling that is available today, so we would like to address this gap with better tooling.

## Goals

Provide tooling to help developers answer the following questions:

- Tooling to understand contributions to size on disk
- Tooling to understand what caused a specific dependency to be kept in a trimmed app
- Integrate with existing tooling where possible
- Reuse existing standards where possible
- Usable out of the box by "advanced" external developers
- Size analysis of managed code constructs
- Similar tooling experience for NativeAot and for ILLink

## Non-goals

- Create a new GUI for size analysis
- Create an interactive tool (GUI or command-line) for size analysis
- Tooling that is only usable by .NET runtime developers
- Size analysis of native code constructs
- Subsume all functionality of existing tools used by .NET runtime developers

## Proposal

A build-time flag `DumpSizeInfo` will cause the supported tools (ILCompiler, ILLink) to output a data file in the build output, which contains a description of a dependency graph, with size info for each node. The nodes of the graph will represent managed types, fields, methods, generic instantiations of methods, constant data, or native constructs (for ILCompiler), with a node "kind" tag to distinguish between these. If `PublishTrimmed` or `PublishAot` is not set, it will produce a warning but do nothing else.

We will provide a dotnet global tool, `dotnet-sizeinfo`, which parses this data file, and has various command-line flags that can be used to show information about the size and dependency relationships between nodes in the graph. The tool will have options to:

- Show transitive dependency chains, with a way to filter (to make it easy to determine which nodes caused the inclusion of a certain construct in the output)

- Show the largest namespaces, types, members in the output, with the size in bytes, and filters to limit the results to certain namespaces, types, and members

- Show the dominator tree of the graph, again with a way to filter to the node of interest, with the inclusive size contribution of a node

## Examples

For the following program, here are some examples of how the tool can be used. The size numbers are made-up, and the output only includes members from the code shown (not from framework libraries), for illustration purposes.

```csharp
class Program {
    static void Main() {
        RecursiveA();
        ChainA();
        CallVirtual();
    }

    static void RecursiveA() {
        RecursiveB();
        LargeMethod();
    }

    static void RecursiveB() {
        RecursiveA();
        LargeMethod();
    }

    static void ChainA() => ChainB();

    static void ChainB() => ChainC();

    static void ChainC() => LargeMethod();

    static void LargeMethod() {
        Console.WriteLine("This is a large method with a large(-ish) constant string inside of it.");
    }

    static void CallVirtual() {
        var d = new Derived();
        CallVirtualHelper(d);
    }

    static void CallVirtualHelper(Base b) {
        b.VirtualMethod();
    }
}

class Base {
    public virtual void VirtualMethod();
}

class Derived : Base {
    public override void VirtualMethod() {}
} 

```

```
> dotnet sizeinfo --input path/to/sizeinfo.xml
Size (bytes) | Size (%) | Member
-------------+----------+------------------------
230          |       46 | Program::LargeMethod
50           |       10 | Program::Main
40           |        8 | Program::CallVirtual
40           |        8 | Program::RecursiveA
40           |        8 | Program::RecursiveB
20           |        8 | Program::CallVirtualHelper
20           |        4 | Program::ChainA
20           |        4 | Program::ChainB
20           |        4 | Program::ChainC
10           |        2 | Base::VirtualMethod
10           |        2 | Derived::VirtualMethod
-------------+----------+------------------------
500
```

```
> dotnet sizeinfo dependencies --input path/to/sizeinfo.xml --filter LargeMethod
Size (bytes) | Size (%) | Member
-------------+----------+------------------------
230          |          | Program::LargeMethod
             |          |   Program::RecursiveA
             |          |     Program::RecursiveB
             |          |     Program::Main
             |          |   Program::RecursiveB
             |          |     Program::RecursiveA
             |          |       Program::Main
             |          |   Program::ChainC
             |          |     Program::ChainB
             |          |       Program::ChainA


```


```
> dotnet sizeinfo dominators --input path/to/sizeinfo.xml
Inclusive size (bytes) | Inclusive size (%) | Member
-----------------------+--------------------+---------------------
500                    |                    | Program::Main
                       |                    |   Program::ChainA
                       |                    |     Program::ChainB
                       |                    |       Program::ChainC
                       |                    |   Program::RecursiveA
                       |                    |   Program::RecursiveB
                       |                    |   Program::LargeMethod
                       |                    |   Program::CallVirtual
                       |                    |     Base::VirtualMethod
                       |                    |     Derived::.ctor
                       |                    |     Derived::VirtualMethod
                       |                    |     Program::CallVirtualHelper
```

Notice that the dominator tree here does not match the call graph for virtual methods. In the dependency graph, there is an edge from `Derived::.ctor` to `Derived::VirtualMethod`, and also from `Base::VirtualMethod` to `Derived::VirtualMethod`, so the immediate dominator of `Derived::VirtualMethod` is `Program::CallVirtual` (the common ancestor of `Derived::.ctor` and `Base::VirtualMethod`).

## Challenges

### Large data files

The tool may be slow to execute on a large data file. In some circumstances it would be useful to load the data into memory once, then be able to query it interactively. This is out of scope for the first version of the tool, but we will build the core functionality as a reusable library that could easily be used in another tool to provide this functionality. For example, a .NET Interactive notebook could be used to do the same analysis interactively.

### Virtual methods

Virtual methods introduce a kind of conditional dependency into the graph. A virtual method will be preserved (by ILLink or ILCompiler) if its declaring type is constructed, and there is a call somewhere in the program to the base virtual method. This analysis is intentionally conservative: it will preserve any virtual methods that may be reached, but this can include methods that are unreachable when the program is executed. Representing these kinds of dependencies in the dependency graph is challenging. There are a few options:

1. Treat virtual methods as roots in the analysis
2. Treat any virtual methods as dependencies of virtual callsites matching the method signature
3. Treat virtual methods as dependencies of the constructors of the declaring type
4. Both 2 and 3: treat virtual methods as if they are dependencies both of matching callsites and the declaring type's construtors

All of these approaches may be useful in different circumstances. Whenever a virtual method is contributing to the program size, the author will need to understand whether this dependency is truly required at runtime, or if it is the result of the conservative analysis. Determining this will require looking at the code, not just the output of this tool. However, with approaches 2, 3, and 4, the tool can at least give some indication of why the method was kept. We will experiment with the different approaches to see if one stands out as more useful than others in the dependency analysis.

### Cycles in the program call graph

Recursive or mutually recursive methods may introduce cycles into the call graph (and thus the dependency graph). This is no problem for the dominator tree, which by definition does not have cycles, but developers looking at the dominator tree will need to be aware of how it behaves. Dependencies of nodes that are part of a cycle may be placed closer to the root of the dominator tree than where the actual callsite is in code. For the transitive dependency chains, cycles will be collapsed - so in the dependency chain for a method that is reachable through a set of mutually recursive methods, there will be at most one dependency edge per callsite.

### Generic expansion

ILLink, being an IL rewriter, does not expand generics, so there is no potential for generic expansion to lead to an increase in size on disk. ILCompiler, however, expands generics with value-type type arguments into native code. For ILCompiler, these expansions will be included as separate nodes in the dependency graph.

### De-mangling names

Size analysis tools which analyze native binaries have an additional challenge because they will only see mangled names of functions. We will avoid this problem by having the compiler output a data file with the unmangled names.

### Accurate size info

### Representing native constructs



###

Because the conservative analysis 

matching the signature of a virtual callsite as dependencies. This can introduce "conservative" dependencies that don't actually represent possible executions of the program.
- Treat virtual methods as dependencies of the constructor of the type. This 

We can later experiment with different ways to represent these virtual calls - for example, it might be useful to treat any virtual method matching the signature of a virtual callsite as a dependency of the calling method (even if no execution of the program )

virtual methods callees as dependencies of a virtual callsite (even when no execution of the program could reach a particual)




Rather than understanding and decompiling native images or managed metadata, we will build tooling that relies on data provided by the compiler. Our various compilers will need to support the same output formats.



There are two general approaches that c

On the production side, it needs to be easy to collect the required information from our build tooling. There are two general approaches:

1. 

Ideally there would be a single MSBuild property which would turn on the collection, that works both for NativeAot and ILLink. It should produce the same file format in both scenarios, to support a uniform experience whether using `PublishTrimmed` or `PublishAot`.

The produced data needs to include size information, and dependency information.

## App size



## Existing tooling

### Producing size/dependency info

- ILCompiler dgml log

  The MSBuild property `IlcGenerateDgmlFile` can be set to produce a DGML log recording the dependency graph of the NativeAot compilation.

- ILCompiler ETW logs

  On Windows, ILCompiler can emit ETW events that represent the dependency graph of a NativeAot compilation.

- ILCompiler "mstat" output
  
  The MSbuild property `IlcGenerateMstatFile` can be set to produce a summary of size info of the NativeAot compilation. The output is a managed assembly with size info encoded in the instruction stream, and can be read using standard APIs such as System.Reflection.Metadata.

- ILLink dependency recorder

  The MSBuild property `_TrimmerDumpDependencies` can be set to produce an XML log recording the dependency graph of trimming done by ILLink. The output format can be a plain XML file or DGML.

### Reading and analyzing size/dependency info

- [DependencyGraphViewer](https://github.com/dotnet/runtime/tree/main/src/coreclr/tools/aot/DependencyGraphViewer)

  This is a WinForms app that can be built from dotnet/runtime. It is able to record the ETW events from ILCompiler, or load a dgml produced by ILCompiler or ILLink. The UI lets you explore the dependency graph by showing a window for the current node, with a list of incoming and outgoing edges that can be clicked on to show a window for the referenced node.

- [ILLink Analyzer](https://github.com/dotnet/runtime/blob/main/src/tools/illink/src/analyzer/README.md)

  This command-line tool (which can also be built from dotnet/runtime) can parse the plain XML output of the ILLink dependency recorder, and has a few flags to print out dependencies on a given node, root nodes, count of nodes by type (types/fields/methods, etc.), and the IL size per node.

- MStat reading tools

  The Mstat format produced by ILCompiler can be read with Cecil or SRM, and there have been various tools built on this, from ad-hoc tools to dump info to the command-line, to tools which show the same info in a web ui: https://github.com/ShreyasJejurkar/MstatReader.

### Related external tooling

- [Bloaty](https://github.com/google/bloaty) (Google)

  Command-line tool that can print out a size breakdown of native binaries (ELF, Mach-O, PE). The breakdown is by native image sections, the memory segments defined for the runtime loader, and (with debug symbols) source files. It also has support for size diffs, and name demangling rules for C++.

- [twiggy](https://github.com/rustwasm/twiggy)

  Command-line tool for rust-wasm that shows the size breakdown and call graph dependencies of wasm binaries. It can show size per function, paths in the call graph which depend on a specific function, monomorphized functions that contribute to the binary size, a dominator tree of the call graph with inclusive sizes. It also has support for size diffs.

- [cargo bloat](https://github.com/RazrFalcon/cargo-bloat)

  Inspired by bloaty, this command-line tool also understands native binaries (ELF, Mach-O, PE). It can show the largest functions in the file, the biggest crate dependencies, or the crate dependencies which took the longest time to compile.

### General-purpose profile viewers

### Comparison

The tool which is most similar to what we would like to provide is twiggy.