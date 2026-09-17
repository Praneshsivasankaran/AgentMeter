#include "native.h"
#include <libproc.h>
#include <sys/sysctl.h>
#include <unistd.h>
#include <string.h>
#include <stdlib.h>
#include <sys/stat.h>
// Reads only the requested prefix. Never requests ARG_MAX or traverses environment.
static int prefix(int pid,unsigned char*b,size_t n){int m[]={CTL_KERN,KERN_PROCARGS2,pid};size_t s=n;memset(b,0,n);int r=sysctl(m,3,b,&s,0,0);return r==0?(int)s:0;}
#include "classifier.inc"
int am_identity(int pid,uint64_t*sec,uint64_t*usec,int*ppid){struct proc_bsdinfo b;int n=proc_pidinfo(pid,PROC_PIDTBSDINFO,0,&b,sizeof b);if(n!=sizeof b)return 0;*sec=b.pbi_start_tvsec;*usec=b.pbi_start_tvusec;*ppid=b.pbi_ppid;return 1;}
int am_scan(Candidate*out,int cap,const char*codex,const char*claude){pid_t ids[16384];int bytes=proc_listpids(PROC_UID_ONLY,getuid(),ids,sizeof ids),count=0;for(int j=0;j<bytes/(int)sizeof(pid_t)&&count<cap;j++){struct proc_bsdinfo bi;if(proc_pidinfo(ids[j],PROC_PIDTBSDINFO,0,&bi,sizeof bi)!=sizeof bi)continue;if(bi.e_tdev==(uint32_t)-1)continue;char path[4096];if(proc_pidpath(ids[j],path,sizeof path)<=0)continue;int p=strcmp(path,codex)==0?1:(strcmp(path,claude)==0?2:0);if(!p)continue;Candidate c={.pid=ids[j],.provider=p};if(!am_identity(c.pid,&c.sec,&c.usec,&c.ppid))continue;struct kinfo_proc k={0};int m[]={CTL_KERN,KERN_PROC,KERN_PROC_PID,c.pid};size_t s=sizeof k;c.tty=sysctl(m,4,&k,&s,0,0)==0&&s==sizeof k&&k.kp_eproc.e_tdev!=(dev_t)-1;struct vnode_fdinfo vi={0};
 if(proc_pidfdinfo(c.pid,0,PROC_PIDFDVNODEINFO,&vi,sizeof vi)!=sizeof vi || !S_ISCHR(vi.pvi.vi_stat.vst_mode) || vi.pvi.vi_stat.vst_rdev!=bi.e_tdev)c.tty=0;
 c.mode=-2;out[count++]=c;}return count;}

#include <errno.h>
extern char **environ;
int am_spawn(const char *path,char *const args[],char *const overrides[],const char *cwd,int *input,int *output,int *error){
 int in[2]={-1,-1},out[2]={-1,-1},err[2]={-1,-1};
 if(pipe(in)||pipe(out)||pipe(err))goto failure;
 int fds[]={in[0],in[1],out[0],out[1],err[0],err[1]};
 for(int i=0;i<6;i++)fcntl(fds[i],F_SETFD,FD_CLOEXEC);
 posix_spawn_file_actions_t a;posix_spawn_file_actions_init(&a);
 posix_spawn_file_actions_adddup2(&a,in[0],0);posix_spawn_file_actions_adddup2(&a,out[1],1);posix_spawn_file_actions_adddup2(&a,err[1],2);
 for(int i=0;i<6;i++)posix_spawn_file_actions_addclose(&a,fds[i]);
 if(__builtin_available(macOS 26.0,*))posix_spawn_file_actions_addchdir(&a,cwd);else posix_spawn_file_actions_addchdir_np(&a,cwd);
 posix_spawnattr_t attr;posix_spawnattr_init(&attr);posix_spawnattr_setflags(&attr,POSIX_SPAWN_SETPGROUP|POSIX_SPAWN_SETSIGMASK|POSIX_SPAWN_CLOEXEC_DEFAULT);posix_spawnattr_setpgroup(&attr,0);sigset_t mask;sigemptyset(&mask);posix_spawnattr_setsigmask(&attr,&mask);
 // Inherit provider-owned environment opaquely, replacing only explicit child settings.
 int count=0,extra=0;while(environ[count])count++;while(overrides[extra])extra++;
 char **env=calloc((size_t)(count+extra+1),sizeof(char*));
 if(!env){posix_spawn_file_actions_destroy(&a);posix_spawnattr_destroy(&attr);goto failure;}
 int n=0;for(int i=0;i<count;i++){int replace=strncmp(environ[i],"GIT_",4)==0;for(int j=0;j<extra;j++){const char *eq=strchr(overrides[j],'=');if(eq&&!strncmp(environ[i],overrides[j],(size_t)(eq-overrides[j]+1)))replace=1;}if(!replace)env[n++]=environ[i];}
 for(int j=0;j<extra;j++)env[n++]=overrides[j];
 pid_t pid;int result=posix_spawn(&pid,path,&a,&attr,args,env);free(env);posix_spawn_file_actions_destroy(&a);posix_spawnattr_destroy(&attr);
 if(result){errno=result;goto failure;}
 close(in[0]);close(out[1]);close(err[1]);*input=in[1];*output=out[0];*error=err[0];fcntl(*input,F_SETFL,O_NONBLOCK);fcntl(*output,F_SETFL,O_NONBLOCK);fcntl(*error,F_SETFL,O_NONBLOCK);return pid;
 failure:;int saved=errno;int cleanup[]={in[0],in[1],out[0],out[1],err[0],err[1]};for(int i=0;i<6;i++)if(cleanup[i]>=0)close(cleanup[i]);errno=saved;return -1;
}
void am_group_signal(int pid,int sig){if(pid>1)kill(-pid,sig);}
int am_reap(int pid){int status;pid_t r;do{r=waitpid(pid,&status,0);}while(r<0&&errno==EINTR);return r<0?-1:(WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status));}
uint64_t am_rss(int pid){struct proc_taskinfo t;if(proc_pidinfo(pid,PROC_PIDTASKINFO,0,&t,sizeof t)!=sizeof t)return 0;return t.pti_resident_size;}

int am_has_exited(int pid){siginfo_t info={0};if(waitid(P_PID,(id_t)pid,&info,WEXITED|WNOHANG|WNOWAIT)<0)return -1;return info.si_pid==pid;}

uint64_t am_group_rss(int group){pid_t ids[16384];int size=proc_listpids(PROC_UID_ONLY,getuid(),ids,sizeof ids);uint64_t total=0;for(int i=0;i<size/(int)sizeof(pid_t);i++){struct proc_bsdinfo b;if(proc_pidinfo(ids[i],PROC_PIDTBSDINFO,0,&b,sizeof b)==sizeof b&&b.pbi_pgid==(uint32_t)group)total+=am_rss(ids[i]);}return total;}
