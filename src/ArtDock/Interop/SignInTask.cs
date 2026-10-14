using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CSharp.RuntimeBinder;

namespace ArtDock.Interop;

/// <summary>What the sign-in task starts: a program, and the arguments it is given.</summary>
public readonly record struct SignInCommand(string Path, string Arguments)
{
    /// <summary>The same program, by its path as Windows compares paths, with the same arguments.</summary>
    public bool Matches(SignInCommand other) =>
        string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Arguments, other.Arguments, StringComparison.Ordinal);
}

/// <summary>
/// The task that starts the dock as the user signs in, which <see cref="Autostart"/> keeps in
/// step with the Run entry.
/// </summary>
/// <remarks>
/// An interface so that the tests can keep one without Task Scheduler: they must never write
/// the user's real task, as they never write the real Run key.
/// </remarks>
public interface ISignInTask
{
    /// <summary>What the task starts, or null when there is no task or it could not be read.</summary>
    SignInCommand? Read();

    /// <summary>Has the task start <paramref name="command"/>, making it if there is none.</summary>
    void Write(SignInCommand command);

    /// <summary>Takes the task away, if there is one.</summary>
    void Remove();
}

/// <summary>
/// A Task Scheduler task that starts the dock as the user signs in, ahead of the startup apps
/// Explorer starts from the Run key (TODO.md item 35).
/// </summary>
/// <remarks>
/// <para>
/// Explorer starts the Run key's entries one after another, in the key's order, after a delay of
/// its own. Read off a sign-in on 2026-10-11, the dock — ninth of ten — came up 45 s after the
/// desktop, 27 s of it waiting on the entry before. A logon trigger fires with the sign-in
/// instead. The Run entry stays, and stays the switch: Task Manager's <em>Startup apps</em> and
/// the Settings app list it, and never a task — see <see cref="Autostart.Refresh()"/>.
/// </para>
/// <para>
/// A standard user may register a task with a logon trigger for themselves, with no elevation —
/// measured on the development machine the same day. The name carries the user's SID because
/// task names are the machine's, not the user's.
/// </para>
/// <para>
/// A task's defaults are wrong for a dock, and each is set against its default here: below
/// normal priority, which a process started at it hands on to everything it starts; stopped
/// after three days; and not started, or stopped, on battery.
/// </para>
/// <para>
/// Through Task Scheduler's scripting interface, late bound, rather than <c>schtasks.exe</c>: a
/// read at every start of the dock would otherwise be a process, and the install hook has
/// seconds.
/// </para>
/// </remarks>
public sealed class SignInTask : ISignInTask
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary><c>TASK_CREATE_OR_UPDATE</c>.</summary>
    private const int CreateOrUpdate = 6;

    /// <summary><c>TASK_LOGON_INTERACTIVE_TOKEN</c>: as the user signed in, with no password kept.</summary>
    private const int InteractiveToken = 3;

    private static readonly Lazy<SignInTask?> _forThisUser = new(() =>
        WindowsIdentity.GetCurrent().User?.Value is { Length: > 0 } sid ? new SignInTask(sid) : null);

    private readonly string _userSid;

    public SignInTask(string userSid) => _userSid = userSid;

    /// <summary>The task of the user this process runs as; null if Windows will not say who that is.</summary>
    public static ISignInTask ForThisUser => (ISignInTask?)_forThisUser.Value ?? None.Instance;

    /// <summary>What the task is called in Task Scheduler's library: the product and the user's SID.</summary>
    public static string NameFor(string userSid) => $"ArtDock {userSid}";

    public SignInCommand? Read()
    {
        try
        {
            var folder = Folder();
            return folder is null ? null : CommandOf((string)folder.GetTask(NameFor(_userSid)).Xml);
        }
        catch (Exception e) when (IsTaskSchedulerFailure(e))
        {
            // No task — Task Scheduler's answer is ERROR_FILE_NOT_FOUND — or none that can be read.
            return null;
        }
    }

    public void Write(SignInCommand command)
    {
        try
        {
            Folder()?.RegisterTask(
                NameFor(_userSid), Xml(command, _userSid), CreateOrUpdate, null, null, InteractiveToken, null);
        }
        catch (Exception e) when (IsTaskSchedulerFailure(e))
        {
            // Left to the Run entry, which starts the dock as it always has, later.
        }
    }

    public void Remove()
    {
        try
        {
            Folder()?.DeleteTask(NameFor(_userSid), 0);
        }
        catch (Exception e) when (IsTaskSchedulerFailure(e))
        {
            // None there, or not ours to delete. Either way a task that is left starts a dock
            // that leaves again at once: see Autostart.StartsThisCopy.
        }
    }

    /// <summary>
    /// The task's definition, in Task Scheduler's XML: a logon trigger for the user, run as them
    /// with no elevation, at normal priority, for as long as it runs, on battery or not.
    /// </summary>
    public static string Xml(SignInCommand command, string userSid)
    {
        XNamespace ns = TaskNamespace;

        // Quoted, as Task Scheduler's own dialog quotes it: the path may have a space in it —
        // a user folder named for its owner. The working directory never is, or it is not found.
        return new XElement(
            ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo", new XElement(ns + "Author", "ArtDock")),
            new XElement(
                ns + "Triggers",
                new XElement(
                    ns + "LogonTrigger",
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "UserId", userSid))),
            new XElement(
                ns + "Principals",
                new XElement(
                    ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", userSid),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(
                ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(ns + "Priority", "4"),
                new XElement(ns + "Enabled", "true")),
            new XElement(
                ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(
                    ns + "Exec",
                    new XElement(ns + "Command", $"\"{command.Path}\""),
                    new XElement(ns + "Arguments", command.Arguments),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(command.Path) ?? "")))
        ).ToString();
    }

    /// <summary>
    /// What a task's definition starts — its first program, unquoted, and that program's
    /// arguments — or null when it starts none.
    /// </summary>
    public static SignInCommand? CommandOf(string xml)
    {
        XNamespace ns = TaskNamespace;

        try
        {
            var exec = XDocument.Parse(xml).Descendants(ns + "Exec").FirstOrDefault();
            if (exec?.Element(ns + "Command")?.Value.Trim().Trim('"') is not { Length: > 0 } path)
            {
                return null;
            }

            return new SignInCommand(path, exec.Element(ns + "Arguments")?.Value.Trim() ?? "");
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Task Scheduler's root folder, or null where there is no Task Scheduler to ask.</summary>
    private static dynamic? Folder()
    {
        if (Type.GetTypeFromProgID("Schedule.Service") is not { } type || Activator.CreateInstance(type) is not { } created)
        {
            return null;
        }

        dynamic service = created;
        service.Connect();
        return service.GetFolder(@"\");
    }

    /// <summary>
    /// What asking Task Scheduler can throw: the service's own errors, a missing task as a missing
    /// file, a refusal, or the late binding finding something other than it expected.
    /// </summary>
    private static bool IsTaskSchedulerFailure(Exception e) =>
        e is COMException or IOException or UnauthorizedAccessException or InvalidCastException
            or RuntimeBinderException or ArgumentException;

    /// <summary>No task at all, for a process Windows will not name the user of.</summary>
    private sealed class None : ISignInTask
    {
        public static readonly None Instance = new();

        public SignInCommand? Read() => null;

        public void Write(SignInCommand command)
        {
        }

        public void Remove()
        {
        }
    }
}
