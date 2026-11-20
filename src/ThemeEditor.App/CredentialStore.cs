// --------------------------------------------------
// Umami 3DS Theme Editor - CredentialStore.cs
// --------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ThemeEditor.App
{
    /// <summary>Secrets kept by name (an account) for this app.</summary>
    public interface ICredentialStore
    {
        /// <summary>Whether this system has somewhere safe to keep secrets.</summary>
        bool IsAvailable { get; }

        string Read(string account);

        /// <summary>Stores (or replaces) the secret; false when it could not be stored.</summary>
        bool Write(string account, string secret);

        void Delete(string account);
    }

    /// <summary>
    ///     The system's password vault: the Keychain on macOS, the Credential Manager on Windows and the Secret Service
    ///     (GNOME Keyring, KWallet) through <c>secret-tool</c> on Linux. Nothing ever goes into the settings file.
    /// </summary>
    public static class CredentialStore
    {
        public const string Service = "Umami 3DS Theme Editor";

        private static ICredentialStore _system;

        public static ICredentialStore System => _system ?? (_system =
            OperatingSystem.IsMacOS() ? new MacKeychain()
            : OperatingSystem.IsWindows() ? new WindowsCredentials()
            : (ICredentialStore) new SecretTool());

        // ---- macOS: Security.framework generic passwords

        private sealed class MacKeychain : ICredentialStore
        {
            private const string Security = "/System/Library/Frameworks/Security.framework/Security";
            private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

            [DllImport(Security)]
            private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
                uint accountLength, byte[] account, out uint passwordLength, out IntPtr password, out IntPtr item);

            [DllImport(Security)]
            private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
                uint accountLength, byte[] account, uint passwordLength, byte[] password, IntPtr item);

            [DllImport(Security)]
            private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);

            [DllImport(Security)]
            private static extern int SecKeychainItemDelete(IntPtr item);

            [DllImport(Security)]
            private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

            [DllImport(CoreFoundation)]
            private static extern void CFRelease(IntPtr item);

            private static readonly byte[] ServiceBytes = Encoding.UTF8.GetBytes(Service);

            public bool IsAvailable => true;

            private static bool Find(string account, out IntPtr item, out string secret)
            {
                var name = Encoding.UTF8.GetBytes(account);
                secret = null;
                if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint) ServiceBytes.Length, ServiceBytes, (uint) name.Length, name,
                        out var length, out var data, out item) != 0)
                    return false;
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int) length);
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                secret = Encoding.UTF8.GetString(bytes);
                return true;
            }

            public string Read(string account)
            {
                if (!Find(account, out var item, out var secret))
                    return null;
                CFRelease(item);
                return secret;
            }

            public bool Write(string account, string secret)
            {
                var data = Encoding.UTF8.GetBytes(secret);
                if (Find(account, out var item, out _))
                {
                    var status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint) data.Length, data);
                    CFRelease(item);
                    return status == 0;
                }
                var name = Encoding.UTF8.GetBytes(account);
                return SecKeychainAddGenericPassword(IntPtr.Zero, (uint) ServiceBytes.Length, ServiceBytes, (uint) name.Length, name,
                    (uint) data.Length, data, IntPtr.Zero) == 0;
            }

            public void Delete(string account)
            {
                if (!Find(account, out var item, out _))
                    return;
                SecKeychainItemDelete(item);
                CFRelease(item);
            }
        }

        // ---- Windows: Credential Manager (generic credentials of the signed-in user)

        private sealed class WindowsCredentials : ICredentialStore
        {
            private const int CredTypeGeneric = 1;
            private const int CredPersistLocalMachine = 2;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct Credential
            {
                public int Flags;
                public int Type;
                public string TargetName;
                public string Comment;
                public long LastWritten;
                public int CredentialBlobSize;
                public IntPtr CredentialBlob;
                public int Persist;
                public int AttributeCount;
                public IntPtr Attributes;
                public string TargetAlias;
                public string UserName;
            }

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredWriteW(ref Credential credential, int flags);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredDeleteW(string target, int type, int flags);

            [DllImport("advapi32.dll")]
            private static extern void CredFree(IntPtr buffer);

            public bool IsAvailable => true;

            private static string Target(string account) => Service + ":" + account;

            public string Read(string account)
            {
                if (!CredReadW(Target(account), CredTypeGeneric, 0, out var pointer))
                    return null;
                try
                {
                    var credential = Marshal.PtrToStructure<Credential>(pointer);
                    var bytes = new byte[credential.CredentialBlobSize];
                    Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    CredFree(pointer);
                }
            }

            public bool Write(string account, string secret)
            {
                var bytes = Encoding.UTF8.GetBytes(secret);
                var blob = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, blob, bytes.Length);
                    var credential = new Credential
                    {
                        Type = CredTypeGeneric,
                        TargetName = Target(account),
                        UserName = account,
                        CredentialBlob = blob,
                        CredentialBlobSize = bytes.Length,
                        Persist = CredPersistLocalMachine
                    };
                    return CredWriteW(ref credential, 0);
                }
                finally
                {
                    Marshal.FreeHGlobal(blob);
                }
            }

            public void Delete(string account)
            {
                CredDeleteW(Target(account), CredTypeGeneric, 0);
            }
        }

        // ---- Linux: libsecret's command line tool (the secret goes through stdin, never the command line)

        private sealed class SecretTool : ICredentialStore
        {
            private bool? _available;

            public bool IsAvailable => _available ?? (_available = Exists()).Value;

            private static bool Exists()
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in path.Split(Path.PathSeparator))
                    if (dir.Length > 0 && File.Exists(Path.Combine(dir, "secret-tool")))
                        return true;
                return false;
            }

            private static bool Run(string input, out string output, params string[] args)
            {
                output = null;
                try
                {
                    var info = new ProcessStartInfo("secret-tool")
                    {
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    };
                    foreach (var a in args)
                        info.ArgumentList.Add(a);
                    using (var p = Process.Start(info))
                    {
                        if (p == null)
                            return false;
                        if (input != null)
                            p.StandardInput.Write(input);
                        p.StandardInput.Close();
                        output = p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(15000))
                        {
                            p.Kill();
                            return false;
                        }
                        return p.ExitCode == 0;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }

            public string Read(string account)
            {
                return Run(null, out var output, "lookup", "service", Service, "account", account) && output.Length > 0
                    ? output
                    : null;
            }

            public bool Write(string account, string secret)
            {
                return Run(secret, out _, "store", "--label=" + Service + " (" + account + ")", "service", Service, "account", account);
            }

            public void Delete(string account)
            {
                Run(null, out _, "clear", "service", Service, "account", account);
            }
        }
    }
}
