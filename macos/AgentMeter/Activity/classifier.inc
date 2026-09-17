#include <spawn.h>
#include <poll.h>
#include <signal.h>
#include <sys/wait.h>
#include <fcntl.h>
#include <stdio.h>
#include <time.h>
static char aliases[2][4096];
void am_set_alias(int provider,const char *path){if(provider>=1&&provider<=2)strlcpy(aliases[provider-1],path,sizeof aliases[0]);}
static const char *cx[]={"--no-alt-screen","--strict-config","--oss","--approve-for-me","--search","--sandbox","-s",NULL};
static const char *cl[]={"--safe-mode","--verbose","--no-chrome","--strict-mcp-config","--disable-slash-commands","--effort",NULL};
static const char *sandboxValues[]={"read-only","workspace-write","danger-full-access",NULL};
static const char *effortValues[]={"low","medium","high","xhigh","max",NULL};
// The same finite grammar is used by streaming metadata and synthetic unit tests.
static int step(int provider,const char *token,size_t length,int pending){
 const char **list=pending?(provider==1?sandboxValues:effortValues):(provider==1?cx:cl);
 int match=0;for(int j=0;list[j];j++)if(strlen(list[j])==length&&!memcmp(token,list[j],length))match=1;if(!match)return -1;
 if(pending)return 0;
 return (length==9&&!memcmp(token,"--sandbox",9))||(length==2&&!memcmp(token,"-s",2))||(length==8&&!memcmp(token,"--effort",8));
}
int am_classify_options(int provider,int argc,const char *const args[]){
 if(provider<1||provider>2||argc<1||argc>16)return 0;int pending=0;
 for(int i=1;i<argc;i++){pending=step(provider,args[i],strlen(args[i]),pending);if(pending<0)return 0;}
 return pending==0;
}
static double nowsec(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return t.tv_sec+t.tv_nsec/1e9;}
typedef struct {int pid,mode;uint64_t sec,usec;} Cache;
static Cache cache[256];
void am_forget(int pid){for(int i=0;i<256;i++)if(cache[i].pid==pid)cache[i].pid=0;}
// ps exports arguments only (never -e). Stream one byte at a time; retain only
// prefixes of known literals. Unknown payload is rejected at its first byte.
// Exact argc and single separators reject flattened-argv whitespace ambiguity.
static int options(int pid,int provider,int argc){
 char path[4096];if(proc_pidpath(pid,path,sizeof path)<=0)return 0;
 const char *names[]={path,aliases[provider-1],provider==1?"codex":"claude",NULL};
 const char **flags=provider==1?cx:cl;char pidstr[24];snprintf(pidstr,sizeof pidstr,"%d",pid);
 int f[2];if(pipe(f))return 0;posix_spawn_file_actions_t a;posix_spawn_file_actions_init(&a);
 posix_spawn_file_actions_adddup2(&a,f[1],1);posix_spawn_file_actions_addclose(&a,f[0]);posix_spawn_file_actions_addclose(&a,f[1]);posix_spawn_file_actions_addopen(&a,2,"/dev/null",O_WRONLY,0);
 char *args[]={"/bin/ps","-ww","-p",pidstr,"-o","args=",NULL};char *env[]={"PATH=/usr/bin:/bin","LC_ALL=C",NULL};pid_t child;
 posix_spawnattr_t attr;posix_spawnattr_init(&attr);posix_spawnattr_setflags(&attr,POSIX_SPAWN_CLOEXEC_DEFAULT);int err=posix_spawn(&child,"/bin/ps",&a,&attr,args,env);posix_spawnattr_destroy(&attr);posix_spawn_file_actions_destroy(&a);close(f[1]);if(err){close(f[0]);return 0;}
 double deadline=nowsec()+.25;char token[4096];size_t len=0;int tokens=0,ok=0,bytes=0,pending=0;
 while(nowsec()<deadline && bytes++<4096){struct pollfd p={.fd=f[0],.events=POLLIN};int ms=(int)((deadline-nowsec())*1000);if(ms<1||poll(&p,1,ms)<=0)break;char c;if(read(f[0],&c,1)!=1)break;
 const char **allowed=tokens==0?names:(pending?(provider==1?sandboxValues:effortValues):flags);
 if(c==' '||c=='\n'){int match=0;for(int j=0;allowed[j];j++)if(strlen(allowed[j])==len&&!memcmp(token,allowed[j],len))match=1;if(!match)break;token[len]=0;if(tokens>0){pending=step(provider,token,len,pending);if(pending<0)break;}tokens++;len=0;if(c=='\n'){ok=tokens==argc&&!pending;break;}if(tokens>=argc)break;}
 else {if(len>=sizeof token-1)break;int match=0;for(int j=0;allowed[j];j++)if(strlen(allowed[j])>len&&!memcmp(token,allowed[j],len)&&allowed[j][len]==c)match=1;if(!match)break;token[len++]=c;}
 }
 memset(token,0,sizeof token);close(f[0]);int status=0;if(waitpid(child,&status,WNOHANG)==0){kill(child,SIGKILL);waitpid(child,&status,0);}return ok;
}
int am_mode(int pid,int provider){
 uint64_t s,u;int pp;if(!am_identity(pid,&s,&u,&pp))return 0;
 for(int i=0;i<256;i++)if(cache[i].pid==pid&&cache[i].sec==s&&cache[i].usec==u)return cache[i].mode;
 unsigned char h[8]={0};int argc=0;if(prefix(pid,h,sizeof h)<8)return 0;memcpy(&argc,h,sizeof argc);
 int mode=argc==1?1:(argc>1&&argc<=16?options(pid,provider,argc):0);
 int slot=pid%256;cache[slot]=(Cache){pid,mode,s,u};return mode;
}
