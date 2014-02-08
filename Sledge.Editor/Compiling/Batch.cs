using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sledge.Common.Mediator;
using Sledge.DataStructures.Geometric;
using Sledge.DataStructures.MapObjects;
using Sledge.Editor.Documents;
using Sledge.Providers.Map;
using Sledge.Settings.Models;
using Path = System.IO.Path;

namespace Sledge.Editor.Compiling
{
    public class Batch
    {
        public Document Document { get; set; }
        public Game Game { get; set; }
        public Build Build { get; set; }
        public BuildProfile Profile { get; set; }

        public List<BatchCompileStep> Steps { get; set; }
        public string TargetFile { get; set; }
        public string OriginalFile { get; set; }
        public string MapFileName { get { return Path.GetFileNameWithoutExtension(Document.MapFileName); } }

        public Batch(Document document, Build build, BuildProfile profile)
        {
            Document = document;
            Game = document.Game;
            Build = build;
            Profile = profile;

            var workingDir = Path.GetDirectoryName(document.MapFile);
            if (build.WorkingDirectory == CompileWorkingDirectory.SubDirectory && workingDir != null)
            {
                workingDir = Path.Combine(workingDir,  Path.GetFileNameWithoutExtension(document.MapFileName));
            }
            else if (build.WorkingDirectory == CompileWorkingDirectory.TemporaryDirectory || workingDir == null)
            {
                workingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            }

            if (!Directory.Exists(workingDir)) Directory.CreateDirectory(workingDir);
            TargetFile = SaveCordonMap(document, workingDir);
            OriginalFile = document.MapFile;

            var fileFlag = '"' + TargetFile + '"';

            Steps = new List<BatchCompileStep>
            {
                new BatchCompileStep
                {
                    Operation = Path.Combine(build.Path, build.Csg),
                    Flags = (profile.FullCsgParameters + ' ' + fileFlag).Trim()
                },
                new BatchCompileStep
                {
                    Operation = Path.Combine(build.Path, build.Bsp),
                    Flags = (profile.FullBspParameters + ' ' + fileFlag).Trim()
                },
                new BatchCompileStep
                {
                    Operation = Path.Combine(build.Path, build.Vis),
                    Flags = (profile.FullVisParameters + ' ' + fileFlag).Trim()
                },
                new BatchCompileStep
                {
                    Operation = Path.Combine(build.Path, build.Rad),
                    Flags = (profile.FullRadParameters + ' ' + fileFlag).Trim()
                }
            };
        }

        public void Compile()
        {
            var task = Task.Factory.StartNew(DoCompile).ContinueWith(FinishCompile);
        }

        private void DoCompile()
        {
            Mediator.Publish(EditorMediator.CompileStarted, this);
            var logger = new CompileLogTracer();
            foreach (var step in Steps)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(step.Operation, step.Flags)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WorkingDirectory = Path.GetDirectoryName(TargetFile)
                    }
                };
                process.OutputDataReceived += (sender, args) => logger.AddLine(args.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();
            }
            var errFile = Path.ChangeExtension(TargetFile, "err");
            if (File.Exists(errFile))
            {
                logger.AddErrorLine("The following errors were detected:\r\n\r\n" + File.ReadAllText(errFile).Trim());
            }
        }

        private void CopyInto(string extension, string folder)
        {
            foreach (var file in SearchFor(extension))
            {
                var mappedFile = Path.GetFileNameWithoutExtension(Document.MapFileName) + Path.GetExtension(file);
                mappedFile = Path.Combine(folder, mappedFile);
                if (mappedFile == Document.MapFile) mappedFile = Path.Combine(folder, Path.GetFileName(file));
                if (mappedFile == Document.MapFile) continue;
                File.Copy(file, mappedFile, true);
            }
        }

        private bool CompileSucceeded()
        {
            return SearchFor("bsp").Any() && !SearchFor("err").Any();
        }

        private IEnumerable<string> SearchFor(string extension)
        {
            var fname = Path.GetFileNameWithoutExtension(TargetFile);
            return Directory.GetFiles(Path.GetDirectoryName(TargetFile), fname + "*." + extension);
        }

        private void FinishCompile(Task obj)
        {
            if (CompileSucceeded())
            {
                // Compile succeeded (for all we know)
                var gameDir = Game.GetMapDirectory();
                var mapDir = Path.GetDirectoryName(Document.MapFile);
                if (Build.AfterCopyBsp) CopyInto("bsp", gameDir);
                if (Build.CopyBsp) CopyInto("bsp", mapDir);
                if (Build.CopyRes) CopyInto("res", mapDir);
                if (Build.CopyLin) CopyInto("lin", mapDir);
                if (Build.CopyMap) CopyInto("map", mapDir);
                if (Build.CopyPts) CopyInto("pts", mapDir);
                if (Build.CopyLog) CopyInto("log", mapDir);
                if (Build.CopyErr) CopyInto("err", mapDir);

                Mediator.Publish(EditorMediator.CompileFinished, this);
            }
            else
            {
                Mediator.Publish(EditorMediator.CompileFailed, this);
            }
            // Editor subscribes to these messages and calls Complete() on the correct thread.
        }

        private string SaveCordonMap(Document document, string folder)
        {
            var filename = Path.ChangeExtension(document.MapFileName, ".map");
            var map = document.Map;
            if (document.Map.Cordon)
            {
                map = new Map();
                map.WorldSpawn.EntityData = document.Map.WorldSpawn.EntityData.Clone();
                var entities = document.Map.WorldSpawn.GetAllNodesContainedWithin(document.Map.CordonBounds);
                foreach (var mo in entities)
                {
                    var clone = mo.Clone();
                    clone.SetParent(map.WorldSpawn);
                }
                var outside = new Box(map.WorldSpawn.Children.Select(x => x.BoundingBox).Union(new[] {document.Map.CordonBounds}));
                outside = new Box(outside.Start - Coordinate.One, outside.End + Coordinate.One);
                var inside = document.Map.CordonBounds;

                var brush = new Brushes.BlockBrush();

                var cordon = (Solid) brush.Create(map.IDGenerator, outside, null).First();
                var carver = (Solid) brush.Create(map.IDGenerator, inside, null).First();
                cordon.Faces.ForEach(x => x.Texture.Name = "BLACK");

                // Do a carve (TODO: move carve into helper method?)
                foreach (var plane in carver.Faces.Select(x => x.Plane))
                {
                    Solid back, front;
                    if (!cordon.Split(plane, out back, out front, map.IDGenerator)) continue;
                    front.SetParent(map.WorldSpawn);
                    cordon = back;
                }

                if (document.MapFileName.EndsWith(".map"))
                {
                    filename = Path.GetFileNameWithoutExtension(filename) + "_cordon.map";
                }
            }
            map.WorldSpawn.EntityData.SetPropertyValue("wad", string.Join(";", Game.Wads.Select(x => x.Path)));
            filename = Path.Combine(folder, filename);
            MapProvider.SaveMapToFile(filename, map);
            return filename;
        }
    }
}