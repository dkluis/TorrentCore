#include <CoreFoundation/CoreFoundation.h>
#include <CoreServices/CoreServices.h>
#include <errno.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>

__attribute__((used)) static const char kLauncherIdentity[] =
    "com.conadv.torrentcore.webui.launcher";
static volatile sig_atomic_t child_pid = -1;

static void forward_signal(int signal_number)
{
    pid_t pid = (pid_t) child_pid;
    if (pid > 0) {
        (void) kill(pid, signal_number);
    }
}

static int install_signal_handlers(void)
{
    struct sigaction action;
    memset(&action, 0, sizeof(action));
    action.sa_handler = forward_signal;
    sigemptyset(&action.sa_mask);
    const int signals[] = {SIGINT, SIGTERM, SIGHUP, SIGQUIT};
    for (size_t index = 0; index < sizeof(signals) / sizeof(signals[0]); index++) {
        if (sigaction(signals[index], &action, NULL) != 0) {
            fprintf(stderr, "TorrentCoreWebUI launcher could not install signal handler %d: %s\n",
                    signals[index], strerror(errno));
            return -1;
        }
    }
    return 0;
}

static char *main_executable_path(void)
{
    uint32_t size = 0;
    (void) _NSGetExecutablePath(NULL, &size);
    if (size == 0) return NULL;
    char *raw_path = calloc(size, sizeof(char));
    if (raw_path == NULL || _NSGetExecutablePath(raw_path, &size) != 0) {
        free(raw_path);
        return NULL;
    }
    char *resolved_path = realpath(raw_path, NULL);
    free(raw_path);
    return resolved_path;
}

static char *path_relative_to_executable(const char *executable_path, const char *relative_path)
{
    char *directory = strdup(executable_path);
    if (directory == NULL) return NULL;
    char *separator = strrchr(directory, '/');
    if (separator == NULL) {
        free(directory);
        return NULL;
    }
    *separator = '\0';
    size_t length = strlen(directory) + strlen(relative_path) + 2;
    char *candidate = calloc(length, sizeof(char));
    if (candidate != NULL) (void) snprintf(candidate, length, "%s/%s", directory, relative_path);
    free(directory);
    if (candidate == NULL) return NULL;
    char *resolved_path = realpath(candidate, NULL);
    free(candidate);
    return resolved_path;
}

static int register_bundle(const char *executable_path)
{
    char *bundle_path = path_relative_to_executable(executable_path, "../..");
    if (bundle_path == NULL) {
        fprintf(stderr, "TorrentCoreWebUI launcher could not resolve its bundle.\n");
        return 1;
    }
    CFURLRef bundle_url = CFURLCreateFromFileSystemRepresentation(
        kCFAllocatorDefault, (const UInt8 *) bundle_path, (CFIndex) strlen(bundle_path), true);
    free(bundle_path);
    if (bundle_url == NULL) return 1;
    OSStatus status = LSRegisterURL(bundle_url, true);
    CFRelease(bundle_url);
    if (status != noErr) {
        fprintf(stderr, "TorrentCoreWebUI bundle registration failed with status %d.\n", (int) status);
        return 1;
    }
    return 0;
}

static int run_helper(const char *helper_path, const char *working_directory, int argc, char **argv)
{
    char **helper_argv = calloc((size_t) argc + 1, sizeof(char *));
    if (helper_argv == NULL) return 1;
    helper_argv[0] = (char *) helper_path;
    for (int index = 1; index < argc; index++) helper_argv[index] = argv[index];
    if (install_signal_handlers() != 0) {
        free(helper_argv);
        return 1;
    }
    pid_t pid = fork();
    if (pid < 0) {
        fprintf(stderr, "TorrentCoreWebUI launcher could not create its child process: %s\n", strerror(errno));
        free(helper_argv);
        return 1;
    }
    if (pid == 0) {
        if (chdir(working_directory) != 0) {
            fprintf(stderr, "TorrentCoreWebUI launcher could not enter %s: %s\n", working_directory, strerror(errno));
            _exit(126);
        }
        execv(helper_path, helper_argv);
        fprintf(stderr, "TorrentCoreWebUI launcher could not execute %s: %s\n", helper_path, strerror(errno));
        _exit(errno == ENOENT ? 127 : 126);
    }
    child_pid = pid;
    int status = 0;
    while (waitpid(pid, &status, 0) < 0) {
        if (errno != EINTR) {
            child_pid = -1;
            free(helper_argv);
            return 1;
        }
    }
    child_pid = -1;
    free(helper_argv);
    if (WIFEXITED(status)) return WEXITSTATUS(status);
    if (WIFSIGNALED(status)) return 128 + WTERMSIG(status);
    return 1;
}

int main(int argc, char **argv)
{
    char *executable_path = main_executable_path();
    if (executable_path == NULL) return 1;
    if (argc == 2 && strcmp(argv[1], "--register-bundle") == 0) {
        int result = register_bundle(executable_path);
        free(executable_path);
        return result;
    }
    char *helper_path = path_relative_to_executable(
        executable_path, "../Resources/Runtime/TorrentCore.WebUI");
    const char *working_directory = getenv("TORRENTCORE_WEBUI_WORKING_DIRECTORY");
    char current_directory[PATH_MAX];
    if (working_directory == NULL || working_directory[0] == '\0') {
        working_directory = getcwd(current_directory, sizeof(current_directory));
    }
    free(executable_path);
    if (helper_path == NULL || working_directory == NULL || access(helper_path, X_OK) != 0) {
        fprintf(stderr, "TorrentCoreWebUI runtime helper is missing or not executable.\n");
        free(helper_path);
        return 1;
    }
    int result = run_helper(helper_path, working_directory, argc, argv);
    free(helper_path);
    return result;
}
