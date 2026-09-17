#include <stdint.h>
#include <sys/types.h>
typedef struct {int pid,ppid,provider,tty,mode;uint64_t sec,usec;} Candidate;
int am_scan(Candidate*out,int cap,const char*codex,const char*claude);
int am_identity(int pid,uint64_t*sec,uint64_t*usec,int*ppid);
int am_mode(int pid,int provider);
void am_forget(int pid);

void am_set_alias(int provider,const char *path);
int am_spawn(const char *path,char *const args[],char *const overrides[],const char *cwd,int *input,int *output,int *error);
int am_reap(int pid);
void am_group_signal(int pid,int signal);
uint64_t am_rss(int pid);

int am_has_exited(int pid);
int am_classify_options(int provider,int argc,const char *const args[]);

uint64_t am_group_rss(int group);
