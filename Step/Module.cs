﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Module.cs" company="Ian Horswill">
// Copyright (C) 2020 Ian Horswill
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in the
// Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the
// following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Step.Interpreter;
using Step.Parser;

namespace Step
{
    /// <summary>
    /// Stores values of global state variables
    /// </summary>
    [DebuggerDisplay("{" + nameof(Name) + "}")]
    public class Module
    {
        /// <summary>
        /// Stack traces should be generated with Unity rich text markup
        /// </summary>
        public static bool RichTextStackTraces;
        
        #region Fields
        /// <summary>
        /// Table of values assigned by this module to different global variables
        /// </summary>
        private readonly Dictionary<StateVariableName, object> dictionary = new Dictionary<StateVariableName, object>();
        /// <summary>
        /// Parent module to try if a variable can't be found in this module;
        /// </summary>
        public readonly Module Parent;

        /// <summary>
        /// A user-defined procedure that can be called to import the value of a variable
        /// </summary>
        /// <param name="name">Variable to look up</param>
        /// <param name="value">Value found, if any</param>
        /// <returns>True if variable found</returns>
        public delegate bool BindHook(StateVariableName name, out object value);

        /// <summary>
        /// Optional list of hooks to try when a variable can't be found.
        /// </summary>
        private List<BindHook> bindHooks;

        /// <summary>
        /// The global Module that all other modules inherit from by default.
        /// </summary>
        public static readonly Module Global;

        private List<string> loadTimeWarnings;

        /// <summary>
        /// Name of the module for debugging purposes
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Formatting options to use in Call.
        /// </summary>
        public FormattingOptions FormattingOptions = new FormattingOptions();

        /// <summary>
        /// Extension used for source files
        /// </summary>
        public string SourceExtension = ".step";

        /// <summary>
        /// Extension for CSV files
        /// </summary>
        public string CsvExtension = ".csv";
        #endregion

        #region Constructors
        static Module()
        {
            Global = new Module("Global", null);
            Builtins.DefineGlobals();
        }

        /// <summary>
        /// Make a module that inherits from Global
        /// </summary>
        public Module(string name) : this(name, Global)
        { }

        /// <summary>
        /// Make a module with the Global module as parent and load the specified source files into it.
        /// </summary>
        /// <param name="name">Name of the Module, for debugging purposes</param>
        /// <param name="parent">Parent module for this module.  This will usually be Module.Global</param>
        /// <param name="sourceFiles">Definition files to load</param>
        public Module(string name, Module parent, params string[] sourceFiles)
            : this(name, parent)
        {
            foreach (var f in sourceFiles)
                LoadDefinitions(f);
        }

        /// <summary>
        /// Make a module that inherits from the specified parent
        /// </summary>
        public Module(string name, Module parent)
        {
            Parent = parent;
            Name = name;
        }
        #endregion

        #region Accessors
        /// <summary>
        /// Returns the value of the variable with the specified name
        /// </summary>
        /// <param name="variableName">Name (string) of the variable</param>
        /// <returns>Value</returns>
        /// <exception cref="UndefinedVariableException">If getting a variable and it is not listed in this module or its ancestors</exception>
        public object this[string variableName]
        {
            get => this[StateVariableName.Named(variableName)];
            set => this[StateVariableName.Named(variableName)] = value;
        }

        /// <summary>
        /// Returns the value of the global variable with the specified name
        /// </summary>
        /// <param name="v">The variable</param>
        /// <returns>Value</returns>
        /// <exception cref="UndefinedVariableException">If getting a variable and it is not listed in this module or its ancestors</exception>
        public object this[StateVariableName v]
        {
            get => Lookup(v);
            set => dictionary[v] = value;
        }

        /// <summary>
        /// True if this module has its own definition of the specified variable
        /// </summary>
        public bool Defines(string variableName) => Defines(StateVariableName.Named(variableName));

        /// <summary>
        /// True if this module has its own definition of the specified variable
        /// </summary>
        public bool Defines(StateVariableName v) => dictionary.ContainsKey(v);

        private object Lookup(StateVariableName v, bool throwOnFailure = true)
        {
// First see if it's stored in this or some ancestor module
            for (var module = this; module != null; module = module.Parent)
                if (module.dictionary.TryGetValue(v, out var value))
                    return value;

            // Not found in this or any ancestor module; try bind hooks
            for (var module = this; module != null; module = module.Parent)
                if (module.bindHooks != null)
                    foreach (var hook in module.bindHooks)
                        if (hook(v, out var result))
                            return module.dictionary[v] = result;

            if (v.Name == "Mention")
                // if the user hasn't defined a Mention implementation, just use Write.
                return Builtins.WritePrimitive;
            
            // Give up
            if (throwOnFailure)
                throw new UndefinedVariableException(v);
            return null;
        }

        /// <summary>
        /// All the variable bindings in this module.
        /// </summary>
        public IEnumerable<KeyValuePair<StateVariableName, object>> Bindings => dictionary;

        /// <summary>
        /// All CompoundTasks defined in this Module
        /// </summary>
        public IEnumerable<CompoundTask> DefinedTasks =>
            dictionary.Values.Where(x => x is CompoundTask).Cast<CompoundTask>();

        /// <summary>
        /// Find the CompoundTask named by the specified variable, creating one if necessary.
        /// </summary>
        /// <param name="v">Task variable</param>
        /// <param name="argCount">Number of arguments the task is expected to have</param>
        /// <param name="createIfNeeded">If true and variable is unbound, create a new task to bind it to.</param>
        /// <param name="path">Source file of method referencing this task, if relevant</param>
        /// <param name="lineNumber">Source file line number of method referencing this task, if relevant</param>
        /// <returns>The task</returns>
        /// <exception cref="ArgumentException">If variable is defined but isn't a CompoundTask</exception>
        internal CompoundTask FindTask(StateVariableName v, int argCount, bool createIfNeeded = true, string path = "Unknown", int lineNumber = 0)
        {
            CompoundTask Recur(Module m)
            {
                if (m.dictionary.TryGetValue(v, out var value))
                {
                    var task = value as CompoundTask;
                    if (task == null)
                        throw new ArgumentException($"{v.Name} is not a task.  It is defined as {value}.");
                    return task;
                }
                if (m.Parent != null)
                    return Recur(m.Parent);
                return null;
            }

            var t = Recur(this);
            if (t == null)
            {
                if (createIfNeeded)
                {
                    // Define it in this module.
                    t = new CompoundTask(v.Name, argCount);
                    this[v] = t;
                }
                else 
                    return null;
            }
            if (t.ArgCount != argCount)
                throw new ArgumentException($"{Path.GetFileName(path)}:{lineNumber} {v.Name} was defined with {t.ArgCount} arguments, but a method is being added with {argCount} arguments");
            return t;
        }
        #endregion

        /// <summary>
        /// Calls the named task with the specified arguments and returns the text it generates
        /// </summary>
        /// <param name="state">Global variable bindings to use in the call, if any.</param>
        /// <param name="taskName">Name of the task</param>
        /// <param name="args">Arguments to task, if any</param>
        /// <returns>Generated text as one big string, and final values of global variables.  Or null if the task failed.</returns>
        public (string output, State newDynamicState) Call(
            State state, string taskName, params object[] args)
        {
            var maybeTask = this[StateVariableName.Named(taskName)];
            var t = maybeTask as CompoundTask;
            if (t == null)
                throw new ArgumentException($"{taskName} is a task.  Its value is {maybeTask}");
            var output = TextBuffer.NewEmpty();
            var env = new BindingEnvironment(this, null, null, state);

            string result = null;
            State newState = State.Empty;

            if (t.Call(args, output, env, null,
                (o, u, s, predecessor) =>
                {
                    result = o.Output.Untokenize(FormattingOptions);
                    newState = s;
                    MethodCallFrame.CurrentFrame = predecessor;
                    return true;
                }))
                return (result, newState);
            return (null, State.Empty);
        }

        /// <summary>
        /// Calls the named task with the specified arguments and returns the text it generates
        /// </summary>
        /// <param name="taskName">Name of the task</param>
        /// <param name="args">Arguments to task, if any</param>
        /// <returns>Generated text as one big string, and final values of global variables.  Or null if the task failed.</returns>
        public string Call(string taskName, params object[] args)
        {
            var (output, _) = Call(State.Empty, taskName, args);
            return output;
        }

        /// <summary>
        /// Calls the named task with the specified arguments as a predicate and returns true if it succeeds
        /// This call will fail if the task attempts to generate output.
        /// </summary>
        /// <param name="state">Global variable bindings to use in the call, if any.</param>
        /// <param name="taskName">Name of the task</param>
        /// <param name="args">Arguments to task, if any</param>
        public bool CallPredicate(
            State state, string taskName, params object[] args)
        {
            var maybeTask = this[StateVariableName.Named(taskName)];
            var t = maybeTask as CompoundTask;
            if (t == null)
                throw new ArgumentException($"{taskName} is a task.  Its value is {maybeTask}");
            var output = new TextBuffer(0);
            var env = new BindingEnvironment(this, null, null, state);

            return t.Call(args, output, env, null,
                (o, u, s, p) => true);
        }

        /// <summary>
        /// Calls the named task with the specified arguments as a predicate and returns true if it succeeds
        /// This call will fail if the task attempts to generate output.
        /// </summary>
        /// <param name="taskName">Name of the task</param>
        /// <param name="args">Arguments to task, if any</param>
        public bool CallPredicate(string taskName, params object[] args) => CallPredicate(State.Empty, taskName, args);

        private static readonly LocalVariableName FunctionResult = new LocalVariableName("??result", 0);

        /// <summary>
        /// Calls the named task with the specified arguments as a function and returns the value of its last argument
        /// This call will fail if the task attempts to generate output.
        /// </summary>
        /// <param name="state">Global variable bindings to use in the call, if any.</param>
        /// <param name="taskName">Name of the task</param>
        /// <param name="args">Arguments to task, if any</param>
        public T CallFunction<T>(
            State state, string taskName, params object[] args)
        {
            var maybeTask = this[StateVariableName.Named(taskName)];
            var t = maybeTask as CompoundTask;
            if (t == null)
                throw new ArgumentException($"{taskName} is a task.  Its value is {maybeTask}");

            var env = new BindingEnvironment(this, null, null, state);
            var resultVar = new LogicVariable(FunctionResult);
            var extendedArgs = args.Append(resultVar).ToArray();

            var output = new TextBuffer(0);

            BindingList<LogicVariable> bindings = null;

            if (!t.Call(extendedArgs, output, env, null,
                (o, u, s, p) =>
                {
                    bindings = u;
                    return true;
                })) 
                throw new CallFailedException(taskName, args);
            
            // Call succeeded; pull out the binding of the result variable and return it
            var finalEnv = new BindingEnvironment(this, null, bindings, State.Empty);
            var result = finalEnv.CopyTerm(resultVar);
            if (result is LogicVariable)
                // resultVar is unbound or bound to an unbound variable
                throw new ArgumentInstantiationException(taskName, env, extendedArgs);
            return (T) result;
        }

        /// <summary>
        /// Calls the named task with the specified arguments as a function and returns the value of its last argument
        /// This call will fail if the task attempts to generate output.
        /// </summary>
        /// <param name="taskName">Name of the task</param>
        /// <param name="args">Arguments to task, if any</param>
        public T CallFunction<T>(string taskName, params object[] args) => CallFunction<T>(State.Empty, taskName, args);

        /// <summary>
        /// Load all source files in the specified directory
        /// </summary>
        /// <param name="path">Path for the directory</param>
        /// <param name="recursive">If true, load files from all directories in the subtree under path</param>
        public void LoadDirectory(string path, bool recursive = false)
        {
            foreach (var file in Directory.GetFiles(path))
                if (Path.GetExtension(file) == SourceExtension || Path.GetExtension(file) == CsvExtension)
                    LoadDefinitions(file);
            if (recursive)
                foreach (var sub in Directory.GetDirectories(path))
                    LoadDirectory(sub, true);
        }

        /// <summary>
        /// Load the definitions in the specified file
        /// </summary>
        /// <param name="path">Path to the file</param>
        public void LoadDefinitions(string path)
        {
            using (var defs = new DefinitionStream(this, path))
                LoadDefinitions(defs);
        }

        /// <summary>
        /// Load the method definitions from stream into this module
        /// </summary>
        private void LoadDefinitions(DefinitionStream defs)
        {
            foreach (var (task, weight, pattern, locals, chain, flags, path, line) 
                in defs.Definitions)
            {
                if (task.Name == "initially")
                    RunLoadTimeInitialization(pattern, locals, chain, path, line);
                else if (locals == null)
                    // Declaration
                    FindTask(task, pattern.Length, true, path, line).Flags |= flags;
                else
                    FindTask(task, pattern.Length, true, path, line).AddMethod(weight, pattern, locals, chain, flags, path, line);
            }
        }

        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        private void RunLoadTimeInitialization(object[] pattern, LocalVariableName[] locals, Interpreter.Step chain, string path, int line)
        {
            if (pattern.Length != 0)
                throw new SyntaxError("Initially command cannot take arguments", path, line);
            State bindings = State.Empty;
            if (!chain.Try(new TextBuffer(0),
                new BindingEnvironment(this,
                    new MethodCallFrame(null, null, locals.Select(name => new LogicVariable(name)).ToArray(), 
                        null, null)),
                (o, u, d, p ) =>
                {
                    bindings = d;
                    return true;
                },
                null))
                throw new InvalidOperationException($"{Path.GetFileName(path)}:{line} Initialization failed.");
            LoadBindingList(bindings);
        }

        private void LoadBindingList(State state)
        {
            foreach (var pair in state.Bindings)
                if (pair.Key is StateVariableName g)
                    this[g] = pair.Value;
        }

        /// <summary>
        /// Parse and add the method definitions to this module
        /// </summary>
        public void AddDefinitions(params string[] definitions)
        {
            foreach (var s in definitions)
                LoadDefinitions(new DefinitionStream(new StringReader(s), this, null));
        }

        /// <summary>
        /// Make a new module, then parse and add the specified method definitions.
        /// </summary>
        public static Module FromDefinitions(params string[] definitions)
        {
            var m = new Module("anonymous");
            m.AddDefinitions(definitions);
            return m;
        }

        /// <summary>
        /// Parse and run the specified code
        /// </summary>
        /// <param name="code">Code to run.  This will be used as the RHS of a method for the task TopLevelCall</param>
        /// <returns></returns>
        public string ParseAndExecute(string code)
        {
            if (Defines("TopLevelCall"))
                ((CompoundTask) (this["TopLevelCall"])).EraseMethods();
            AddDefinitions($"TopLevelCall: {code}");
            return Call("TopLevelCall");
        }

        /// <summary>
        /// Add a procedure to call when a variable isn't found.
        /// If the procedure returns a value for it, that value is added to the module.
        /// </summary>
        /// <param name="hook">Procedure to use to import variables</param>
        public void AddBindHook(BindHook hook)
        {
            if (bindHooks == null)
                bindHooks = new List<BindHook>();
            bindHooks.Add(hook);
        }

        #region Debugging tools

        /// <summary>
        /// Returns any warnings found by code analysis
        /// </summary>
        public IEnumerable<string> Warnings()
        {
            if (loadTimeWarnings != null)
                foreach (var w in loadTimeWarnings)
                    yield return w;
            foreach (var w in Lint())
                yield return w;
        }

        private IEnumerable<string> Lint()
        {
            foreach (var pair in dictionary.ToArray())  // Copy the dictionary because it might get modified by TaskDefined
            {
                var variable = pair.Key;
                if (pair.Value is CompoundTask task)
                    foreach (var method in task.Methods)
                        for (var step = method.StepChain; step != null; step = step.Next)
                            if (step is Call c && c.Task is StateVariableName g && !TaskDefined(g))
                            {
                                var fileName = method.FilePath == null ? "Unknown" : Path.GetFileName(method.FilePath);
                                yield return
                                    $"{fileName}:{method.LineNumber} {variable.Name} called undefined task {g.Name}";
                            }
            }

            foreach (var t in DefinedTasks)
                if ((t.Flags & CompoundTask.TaskFlags.Main) == 0)
                {
                    var called = false;
                    foreach (var potentialCaller in DefinedTasks)
                        if (Callees(potentialCaller).Contains(t))
                        {
                            called = true;
                            break;
                        }

                    if (!called)
                        yield return RichTextStackTraces?$"<b>{t}</b> is defined but never called.  If this is deliberate, you can add the annotation [main] to {t} to suppress this message.\n" : $"{t} is defined but never called.    If this is deliberate, you can add the annotation [main] to {t} to suppress this message.";
                }
        }

        private bool TaskDefined(StateVariableName stateVariableName)
        {
            if (stateVariableName == null)
                return false;

            var value = Lookup(stateVariableName, false);
            if (value == null)
                return false;

            value = PrimitiveTask.GetSurrogate(value);

            switch (value)
            {
                case null:
                    return false;
                case CompoundTask _:
                case Delegate _:
                case Cons _:
                case bool _:
                    return true;
                default:
                    return false;
            }
        }

        internal void AddWarning(string warning)
        {
            if (loadTimeWarnings == null)
                loadTimeWarnings = new List<string>();
            loadTimeWarnings.Add(warning);
        }

        private object ResolveVariables(object o)
        {
            switch (o)
            {
                case StateVariableName v when dictionary.TryGetValue(v, out var result):
                    return result;
                case LocalVariableName l:
                    return new LogicVariable(l);
                case object[] tuple:
                    return tuple.Select(ResolveVariables).ToArray();
                default:
                    return o;
            }
        }

        private readonly Dictionary<CompoundTask, HashSet<object>> calleeTable = new Dictionary<CompoundTask, HashSet<object>>();
        internal HashSet<object> Callees(CompoundTask t)
        {
            if (calleeTable.TryGetValue(t, out var result))
                return result;
            
            var callees = new HashSet<object>();
            foreach (var c in t.Callees) callees.Add(ResolveVariables(c));

            calleeTable[t] = callees;
            return callees;
        }

        /// <summary>
        /// True if caller has a method that calls callee
        /// </summary>
        /// <param name="caller">Caller (but be a CompoundTask)</param>
        /// <param name="callee">Target of call (can be a CompoundTask or a primitive task, i.e. a delegate)</param>
        public bool TaskCalls(CompoundTask caller, object callee) => Callees(caller).Contains(callee);

        private readonly Dictionary<CompoundTask, object[][]> subtaskTable = new Dictionary<CompoundTask, object[][]>();

        internal object[][] Subtasks(CompoundTask t)
        {
            if (subtaskTable.TryGetValue(t, out var result))
                return result;

            var subtasks = t.Calls.Select(c => c.Arglist.Select(ResolveVariables).Prepend(ResolveVariables(c.Task)).ToArray()).ToArray();

            subtaskTable[t] = subtasks;
            return subtasks;
        }

        /// <summary>
        /// Return a trace of the method calls from the current frame.
        /// </summary>
        public static string StackTrace
        {
            get
            {
                var b = new StringBuilder();
                if (MethodCallFrame.CurrentFrame != null && MethodCallFrame.CurrentFrame.CallerChain != null)
                    foreach (var frame in MethodCallFrame.CurrentFrame.CallerChain)
                        b.AppendLine(frame.GetCallSourceText(MethodCallFrame.CurrentFrame.BindingsAtCallTime));
                return b.ToString();
            }
        }
        #endregion

        /// <summary>
        /// An event handler to be called on every method call.
        /// Used to implement single-stepping in a debugger
        /// </summary>
        public delegate void TraceHandler(MethodTraceEvent traceEvent, Method method, object[] args, TextBuffer output, BindingEnvironment env);

        /// <summary>
        /// An event handler to be called on every method call.
        /// Used to implement single-stepping in a debugger
        /// </summary>
        public TraceHandler Trace;

        /// <summary>
        /// Which event is being traced (a call, success, or failure)
        /// </summary>
        public enum MethodTraceEvent
        {
            /// <summary>
            /// The arguments have been matched to the head of this method and we will now try running its body
            /// </summary>
            Enter,
            /// <summary>
            /// The method succeeded
            /// </summary>
            Succeed,
            /// <summary>
            /// The method failed
            /// </summary>
            MethodFail,
            /// <summary>
            /// The call completely failed
            /// </summary>
            CallFail
        };
        
        internal void TraceMethod(MethodTraceEvent e, Method method, object[] args, TextBuffer output, BindingEnvironment env)
        {
            Trace?.Invoke(e, method, args, output, env);
        }
    }
}
