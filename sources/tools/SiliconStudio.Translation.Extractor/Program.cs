// Copyright (c) 2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using GNU.Getopt;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Core.IO;

namespace SiliconStudio.Translation.Extractor
{
    internal static class Program
    {
        private static readonly LongOpt[] LOpts = {
            new LongOpt("directory", Argument.Required, null, 'D'),
            new LongOpt("recursive", Argument.No, null, 'r'),
            new LongOpt("merge", Argument.No, null, 'm'),
            new LongOpt("default-domain", Argument.Required, null, 'd'),
            new LongOpt("output", Argument.Required, null, 'o'),
            new LongOpt("help", Argument.No, null, 'h'),
            new LongOpt("verbose", Argument.No, null, 'v'),
        };
        private static readonly string SOpts = "-:D:rmd:o:hv";

        private static int Main([NotNull] string[] args)
        {
#if DEBUG
            // Allow to attach debugger
            Console.ReadLine();
#endif // DEBUG
            if (args.Length == 0)
            {
                ShowUsage();
                return -1;
            }

            if (!ParseOptions(args, out var options, out var message))
            {
                Console.WriteLine(message.ToString());
                return -1;
            }

            if (options.ShowUsage)
            {
                ShowUsage();
                return 0;
            }

            if (!CheckOptions(options, out message))
            {
                Console.WriteLine(message.ToString());
                return -1;
            }

            try
            {
                // Compute the list of input files
                ISet<UFile> inputFiles = new HashSet<UFile>();
                foreach (var path in options.InputDirs)
                {
                    foreach (var searchPattern in options.InputFiles)
                    {
                        var files = Directory.GetFiles(path, searchPattern, options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                        foreach (var fileName in files)
                        {
                            inputFiles.Add(new UFile(fileName));
                        }
                    }
                }
                // Extract all messages from the input files
                var messages = new List<Message>();
                messages.AddRange(new CSharpExtractor(inputFiles).ExtractMessages());
                messages.AddRange(new XamlExtractor(inputFiles).ExtractMessages());
                Debug.WriteLine($"Found {messages.Count} messages."); // only show in verbose
                // Export/merge messages
                var exporter = new POExporter(options);
                exporter.Merge(messages);
                exporter.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during execution: {ex.Message}");
                return 1;
            }

            return 0;
        }

        private static bool CheckOptions([NotNull] Options options, [NotNull] out StringBuilder message)
        {
            message = new StringBuilder();
            try
            {
                if (options.InputFiles.Count == 0)
                {
                    // Add all supported formats
                    options.InputFiles.Add("*.cs");
                    options.InputFiles.Add("*.xaml");
                }
                if (options.InputDirs.Count == 0)
                {
                    options.InputDirs.Add(Environment.CurrentDirectory);
                }

                foreach (var dir in options.InputDirs)
                {
                    if (!Directory.Exists(dir))
                    {
                        message.AppendLine($"Input directory '{dir}' not found");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                message.Append(e.Message);
                return false;
            }

            return true;
        }

        private static bool ParseOptions([NotNull] string[] args, [NotNull] out Options options, [NotNull] out StringBuilder message)
        {
            options = new Options();
            message = new StringBuilder();

            try
            {
                var getopt = new Getopt(Assembly.GetExecutingAssembly().GetName().Name, args, SOpts, LOpts) { Opterr = false };
                int option;
                while ((option = getopt.getopt()) != -1)
                {
                    switch (option)
                    {
                        case 1:
                            options.InputFiles.Add(getopt.Optarg);
                            break;

                        case 'D':
                            options.InputDirs.Add(getopt.Optarg);
                            break;

                        case 'r':
                            options.Recursive = true;
                            break;

                        case 'd':
                            options.OutputFile = $"{getopt.Optarg}.pot";
                            break;

                        case 'm':
                            options.Overwrite = false;
                            break;

                        case 'o':
                            options.OutputFile = getopt.Optarg;
                            break;

                        case 'h':
                            options.ShowUsage = true;
                            return true;

                        case 'v':
                            options.Verbose = true;
                            break;

                        case ':':
                            message.AppendLine($"Option '{getopt.OptoptStr}' requires an argument");
                            return false;

                        case '?':
                            message.AppendLine($"Invalid option '{getopt.OptoptStr}'");
                            return false;

                        default:
                            ShowUsage();
                            return false;
                    }
                }

                if (getopt.Opterr)
                {
                    message.AppendLine();
                    message.Append("Error in the command line options. Use -h to display the options usage.");
                    return false;
                }
            }
            catch (Exception e)
            {
                message.Append(e.Message);
                return false;
            }

            return true;
        }

        private static void ShowUsage()
        {
            var newLine = Environment.NewLine;
            Console.Write(
                $"Extract strings from C# or XAML source code files and then creates or updates PO template file{newLine}{newLine}" +
                $"Usage:{newLine}" +
                $"    {Assembly.GetExecutingAssembly().GetName().Name}[.exe] [options] [inputfile | filemask] ...{newLine}{newLine}" +
                $"   -D directory, --directory=directory    Add directory to the list of directories. Source files are searched relative to this list of directories{newLine}" +
                $"                                          Use multiples options to specify more directories{newLine}{newLine}" +
                $"   -r, --recursive                        Process all subdirectories{newLine}{newLine}" +
                $"   -d, --domain-name=name                 Use name.pot for output (instead of messages.pot){newLine}{newLine}" +
                $"   -o file, --output=file                 Write output to specified file (instead of name.po or messages.po) {newLine}{newLine}" +
                $"   -m, --merge                            Merge with existing file instead of overwriting it{newLine}{newLine}" +
                $"   -v, --verbose                          Verbose output{newLine}{newLine}" +
                $"   -h, --help                             Display this help and exit{newLine}"
            );
        }
    }
}
