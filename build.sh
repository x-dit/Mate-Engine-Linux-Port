#!/bin/bash
if [ $# -eq 0 ]; then
    echo "Usage: $0 <output_path>"
    exit 1
fi

file_path="$1"

if [ ! -f "$file_path" ]; then
    echo "Warning: '$file_path' is a directory. Adding '/MateEngineX.x86_64' suffix."
    file_path="$1/MateEngineX.x86_64"
fi

~/Unity/Hub/Editor/6000.2.6f2/Editor/Unity -batchmode -quit -nographics -projectPath $(dirname "$(realpath "${BASH_SOURCE[0]}")") -executeMethod CliBuilder.Build --output $file_path
