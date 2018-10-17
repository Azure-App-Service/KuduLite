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

copy_to_build_dir() {
  rm -rf "$@node_modules"	
  if [ ! -d "node_modules" ]; then
    exit 1
  else
    mkdir -p "$@"
    mkdir -p "$@KuduConsole"
    cp -r node_modules "$@"
    cp -r node_modules "$@/KuduConsole"
  fi
}

printf "\n\nInstalling Kudu Script\n\n" 
echo "$@"
retry npm --loglevel=error install https://github.com/projectkudu/KuduScript/tarball/16de31b5f5ca590ea085979e5fa5e74bb62f647e
copy_to_build_dir "$@"
