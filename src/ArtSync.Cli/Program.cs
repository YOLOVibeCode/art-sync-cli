using ArtSync.Cli;

var argv0 = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
return CliApp.CreateDefault().Run(args, argv0);
