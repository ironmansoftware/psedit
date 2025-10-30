
// Scaffolded RemoteFileManagerService for PSRemoting support
using System;
using System.IO;
using System.Threading.Tasks;

namespace psedit
{
    public class RemoteFileManagerService
    {
        // Example: Fetch a remote file's contents and save to a local temp file
        public async Task<string> FetchRemoteFileAsync(string remoteFilePath, Func<string, Task<byte[]>> fetchContentFunc)
        {
            if (string.IsNullOrEmpty(remoteFilePath)) return null;
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_" + Path.GetFileName(remoteFilePath));
            byte[] content = await fetchContentFunc(remoteFilePath);
            if (content != null)
            {
                await File.WriteAllBytesAsync(tempPath, content);
                return tempPath;
            }
            return null;
        }

        // Example: Save local file contents back to remote
        public async Task<bool> SaveRemoteFileAsync(string localFilePath, Func<string, byte[], Task<bool>> saveContentFunc, string remoteFilePath)
        {
            if (!File.Exists(localFilePath)) return false;
            byte[] content = await File.ReadAllBytesAsync(localFilePath);
            return await saveContentFunc(remoteFilePath, content);
        }
    }
}
