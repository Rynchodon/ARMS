# This scripts builds all the .FBX files in the current directory into .mwm files using MwmBuilder.exe
# You will get Texture warnings while running this script.
#
# the path to MwmBuilder.exe may be supplied:
#		as the first argument
#		in build.ini, which must be in the same directory as this script
# failing that, this script will test for MwmBuilder.exe in this script's directory
# failing that, this script will test for MwmBuilder.exe in the current working directory

import os, os.path, shutil, subprocess, sys

scriptDir = os.path.dirname(os.path.realpath(sys.argv[0]))
buildIni = scriptDir + "\\build.ini"
startDir = os.getcwd()
input = "MwmBuilder\\Content"
output = input + "\\Output"

mwmBuilder = os.devnull

# find MwmBuilder
if not os.path.exists(mwmBuilder):
	if len(sys.argv) >= 2: # from argument
		mwmBuilder = sys.argv[1]
	
	if not os.path.exists(mwmBuilder):
		if (os.path.exists(buildIni)): # from buildIni
			exec(open(buildIni).read())
			mwmBuilder = SpaceEngineers + r"\Tools\MwmBuilder\MwmBuilder.exe"
			
		if not os.path.exists(mwmBuilder):
			inScriptDir = scriptDir + "\\MwmBuilder.exe" # from script dir
			if (os.path.exists(inScriptDir)):
				mwmBuilder = inScriptDir
				
			if not os.path.exists(mwmBuilder):
				inStartDir = startDir + "\\MwmBuilder.exe" # from start dir (CWD)
				if (os.path.exists(inStartDir)):
					mwmBuilder = inStartDir

				if not os.path.exists(mwmBuilder):
					print("ERROR: could not find MwmBuilder.exe")
					sys.exit()

# test current directory contains fbx and xml files
bNoFBX = True
bNoXML = True
for file in os.listdir('.'):
	if file.lower().endswith(".fbx"):
		bNoFBX = False
	else:
		if file.lower().endswith(".xml"):
			bNoXML = False

if bNoFBX or bNoXML:
	print("WARNING: " + os.getcwd() + " does not contain .fbx and .xml files")
	sys.exit()


def createDir(l_dir):
	if not os.path.exists(l_dir):
		os.makedirs(l_dir)

# delete all the files in a directory
def emptyDir(l_dir):
	if os.path.exists(l_dir):
		for file in os.listdir(l_dir):
			if os.path.isfile(l_dir + "\\" + file):
				os.remove(l_dir + "\\" + file)

def copyWithExtension(l_from, l_to, l_ext, overwrite = False):
	createDir(l_to)
	os.chdir(l_from)
	for file in os.listdir('.'):
		if file.lower().endswith(l_ext.lower()):
			outFile = l_to + '\\' + os.path.basename(file)
			if not os.path.exists(outFile) or overwrite:
				shutil.copy2(file, l_to)

# set up directories for MwmBuilder
createDir(input)
createDir(output)
copyWithExtension(startDir, input, ".fbx")
copyWithExtension(startDir, input, ".hkt")
copyWithExtension(startDir, output, ".mwm") # copying.mwm to output allows MwmBuilder to skip unchanged models
copyWithExtension(startDir, input, ".xml")

# run MwmBuilder
mwmBuilderProcess = subprocess.Popen([mwmBuilder, "/s:" + input, "/o:" + output, "/l:" + startDir + "\\MwmBuilder.log"])
mwmBuilderProcess.wait()
copyWithExtension(output, startDir, ".mwm", True)

# cannot delete directories, but we can empty them
emptyDir(startDir + "\\" + input)
emptyDir(startDir + "\\" + output)
