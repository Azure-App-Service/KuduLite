#!/bin/bash        
#title           : updateNodeModules.sh
#description     : Installs the node packages required by Kudu
#author		     : Sanchit Mehta
#date            : 20180816
#version         : 0.1    
#usage		     : sh updateNodeModules.sh
#================================================================================

MAX_RETRIES=5

retry() {
  n=1
  while true; do
    "$@" && break || {
      if [[ $n -lt $max ]]; then
        ((n++))
        echo "Attempt $n out of $MAX_RETRIES"
        sleep 5;
      else
        fail "An error has occured during the npm install."
        exit 1
      fi
    }
  done    
}

check_build_dir() {
  echo "$@/node_modules"
  if [ ! -d "$@/node_modules" ]; then
    exit 1
  fi
}

pwd
printf "\n\nInstalling Kudu Script\n\n" 
echo "$@"
retry npm --loglevel=error install --prefix $@ https://github.com/projectkudu/KuduScript/tarball/16de31b5f5ca590ea085979e5fa5e74bb62f647e
check_build_dir() "$@"
