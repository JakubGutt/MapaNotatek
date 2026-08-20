namespace MapaNotatek.Services;

public static class BackupService
{
    public static void ExportCopy(string sourceFolder, string destinationFolder)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException(sourceFolder);
        }

        Directory.CreateDirectory(destinationFolder);
        foreach (var directory in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, directory);
            Directory.CreateDirectory(Path.Combine(destinationFolder, relative));
        }

        foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, file);
            var dest = Path.Combine(destinationFolder, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(file, dest, overwrite: true);
        }
    }
}
