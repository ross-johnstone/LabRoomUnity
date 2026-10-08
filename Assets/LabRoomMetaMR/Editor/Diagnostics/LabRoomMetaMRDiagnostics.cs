// Editor-only diagnostics for the Meta MR integration, compiled into its own small assembly so it keeps working
// even when game scripts fail to compile.
//  * Compiler errors (all assemblies) and warnings (project assemblies) -> Logs/LabRoomMetaMR_Compile.log
//  * Console errors/exceptions                                         -> Logs/LabRoomMetaMR_Compile.log
//  * Automation: if <project>/Library/LabRoomMetaMR.command exists, each line is executed once and the file is
//    removed. "refresh" re-imports changed files; anything else is passed to
//    LabRoom.MetaMR.EditorTools.LabRoomMetaMRSetup.RunCommand (configure, build-scene, validate, android, apk, ...).
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LabRoom.MetaMR.EditorDiagnostics
{
    [InitializeOnLoad]
    static class LabRoomMetaMRDiagnostics
    {
        static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
        static readonly string LogPath = Path.Combine(ProjectRoot, "Logs", "LabRoomMetaMR_Compile.log");
        static readonly string CommandPath = Path.Combine(ProjectRoot, "Library", "LabRoomMetaMR.command");
        static double nextPoll;

        static LabRoomMetaMRDiagnostics()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024) File.Delete(LogPath);
            }
            catch (IOException) { }

            CompilationPipeline.compilationStarted += _ => Write($"--- compilation started {DateTime.Now:HH:mm:ss}");
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += _ =>
                Write($"--- compilation finished {DateTime.Now:HH:mm:ss}, failed={EditorUtility.scriptCompilationFailed}");
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Poll;
            Write($"--- domain loaded {DateTime.Now:HH:mm:ss}, compilationFailed={EditorUtility.scriptCompilationFailed}");
        }

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            string name = Path.GetFileNameWithoutExtension(assemblyPath);
            bool projectAssembly = name.StartsWith("Assembly-CSharp", StringComparison.Ordinal) || name.StartsWith("LabRoom", StringComparison.Ordinal);
            foreach (var m in messages)
            {
                if (m.type == CompilerMessageType.Error || (projectAssembly && m.type == CompilerMessageType.Warning))
                    Write($"[{m.type}] {name}: {m.file}({m.line},{m.column}): {m.message}");
            }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                var firstFrame = string.IsNullOrEmpty(stackTrace) ? "" : " @ " + stackTrace.Split('\n')[0].Trim();
                Write($"[{type}] {condition}{firstFrame}");
            }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(CommandPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(CommandPath);
                File.Delete(CommandPath);
            }
            catch (IOException) { return; }

            foreach (var raw in lines)
            {
                var cmd = raw.Trim();
                if (cmd.Length == 0) continue;
                Write($"--- command '{cmd}' {DateTime.Now:HH:mm:ss}");
                if (cmd.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    continue;
                }
                var setup = Type.GetType("LabRoom.MetaMR.EditorTools.LabRoomMetaMRSetup, Assembly-CSharp-Editor");
                var run = setup?.GetMethod("RunCommand", BindingFlags.Public | BindingFlags.Static);
                if (run == null)
                {
                    Write("    LabRoomMetaMRSetup is not loaded (scripts not compiled?)");
                    continue;
                }
                try { run.Invoke(null, new object[] { cmd }); }
                catch (TargetInvocationException e) { Write("    command failed: " + e.InnerException); }
            }
        }

        static void Write(string line)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}
