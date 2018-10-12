#!/bin/bash        
#title           : updateNodeModules.sh
#description     : Installs the node packages required by Kudu
#author		     : Sanchit Mehta
#date            : 20180816
#version         : 0.1    
#usage		     : sh updateNodeModules.sh
#================================================================================

MAX_RETRIES = 5

retry() {
  n = 1
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
  rm -rf "$@/node_modules"	
  if [ ! -d "node_modules" ]; then
    exit 1
  else 
    mv node_modules "$@"
  fi
}

retry npm install https://github.com/projectkudu/KuduScript/tarball/6f8085cef949e47a38eef03345d683dc4ca9ad7d
copy_to_build_dir "$@"
