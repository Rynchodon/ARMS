# build.py
#
# This script combines the individual module folders into a single structure
# for Space Engineers to load (and a bunch of other useful deploy tasks)
#
# It will create two mods,
#   "%AppData%\SpaceEngineers\Mods\Autopilot" and
#   "%AppData%\SpaceEngineers\Mods\Autopilot Dev".
#
# The Dev version has logging enabled

import datetime, errno, os.path, shutil, stat, subprocess, sys, xml.etree.ElementTree as ET

# script directories
scriptDir = os.path.dirname(os.path.realpath(sys.argv[0]))
startDir = os.path.split(scriptDir)[0]
buildIni = scriptDir + "\\build.ini"
buildIniTemplate = scriptDir + "\\build-template.ini"
build_model = scriptDir + "\\build-model.py"

# paths files are moved to
finalDir = os.getenv('APPDATA') + '\SpaceEngineers\Mods\Autopilot'
finalDirDev = finalDir + ' Dev'

# do not change or else log file and settings file will be moved
finalScript = finalDir + '\Data\Scripts\Autopilot\\'
finalScriptDev = finalDirDev + '\Data\Scripts\Autopilot\\'

# paths written to file
finalRelDir_icons = 'Textures\GUI\Icons\Cubes\\'
finalRelDir_model = 'Models\Cubes\\' # Large/Small will be added
finalRelDir_texture = 'Textures\Models\Cubes\\'
finalRelDir_texturePanel = 'Textures\Models\\'

# files that will need to be copied and the source (for errors)
toCopy_icons = dict()
toCopy_modelLarge = dict()
toCopy_modelSmall = dict()
toCopy_texture = dict()
toCopy_texturePanel = dict()

# tracks primary model sizes
#     because L.O.D.s are referred to by primary model .xml file
model_size = dict()
# tracks primary model for construction files, extension-less
primary_model = dict()

# arrays to be printed later
errors = []
warning = []
info = []

# in case build.ini is missing variables
mwmBuilder = os.devnull
Zip7 = os.devnull
mwmProcess = []
modules = []


def investigateBadPath(s_printName, s_path):
    if s_path is os.devnull:
        print (s_printName + " set to null device")
    else:
        lastPath = s_path
        while (not os.path.exists(s_path)):
            if (len(s_path) == 0):
                break
            if (s_path[-1] is "\\"):
                s_path = s_path[:-1]
            lastPath = s_path
            s_path = os.path.dirname(s_path)
        errors.append("ERROR: incorrect path to " + s_printName + "\n\tbad path:  " + lastPath + "\n\tgood path: " + s_path)


def createDir(l_dir):
    if not os.path.exists(l_dir):
        #print ("making: "+l_dir)
        os.makedirs(l_dir)


def eraseDir(l_dir):
    if os.path.exists(l_dir):
        #print ("deleting: "+l_dir)
        shutil.rmtree(l_dir)


def parse_sbc(path):
	try:
		tree = ET.parse(path)
	except Exception as e:
		print ('failed to parse: ' + path)
		raise e
		
	root = tree.getroot()
	
	for BlockDefn in root.findall('./CubeBlocks/Definition'):
	
		# Icon
		for Icon in BlockDefn.findall('./Icon'):
			toCopy_icons[(module + '\\' + Icon.text).lower()] = path
			Icon.text = finalRelDir_icons + os.path.basename(Icon.text)
	
		# BlockSize
		blockSize = ''
		for CubeSize in BlockDefn.findall('./CubeSize'):
			blockSize = CubeSize.text
	
		# Primary Model
		for Model in BlockDefn.findall('./Model'):
			primary = (module + '\\' + Model.text).lower()
			if ('large' in blockSize.lower()):
				toCopy_modelLarge[primary] = path
				model_size[primary] = 'Large'
			else:
				toCopy_modelSmall[primary] = path
				model_size[primary] = 'Small'
			Model.text = finalRelDir_model + blockSize + '\\' + os.path.basename(Model.text)
	
		# BuildProgressModels
		for Model in BlockDefn.findall('./BuildProgressModels/Model'):
			file = Model.get('File')
			if (file):
				file_path = (module + '\\' + file).lower()
				if ('large' in blockSize.lower()):
					toCopy_modelLarge[file_path] = path
				else:
					toCopy_modelSmall[file_path] = path
				primary_model[file_path[:-4]] = primary[:-4]
				Model.set('File', finalRelDir_model + blockSize + '\\' + os.path.basename(file))

	# LCD textures
	for texture in root.findall('./LCDTextures/LCDTextureDefinition/TexturePath'):
		toCopy_texturePanel[(module + '\\' + texture.text).lower()] = path
		texture.text = finalRelDir_texturePanel + os.path.basename(texture.text)
	
	# write tree to finalDir
	outDir = finalDir + '\Data\\'
	createDir(outDir)
	outFile = outDir + os.path.basename(path)
	tree.write(outFile, 'utf-8', True)
	
	outDirDev = finalDirDev + '\Data\\'
	createDir(outDirDev)
	shutil.copy2(outFile, outDirDev + os.path.basename(path))


def find_sbc(dataPath):
    for file in os.listdir(dataPath):
        if file.endswith('.sbc'):
            parse_sbc(dataPath + '\\' + file)


def parse_xml(path):
    tree = ET.parse(path)
    root = tree.getroot()

    if (not root.tag == 'Model' or not root.get('Name')):
        return False

    print ('\tparsing xml: '+os.path.basename(path))

    # find textures
    for Parameter in root.iter('Parameter'):
        name = Parameter.get('Name')
        if (name):
            if ('texture' in name.lower()):
                if (Parameter.text):
                    toCopy_texture[(module + '\\' + Parameter.text).lower()] = path
                    Parameter.text = finalRelDir_texture + os.path.basename(Parameter.text)

    outDir = os.path.dirname(path) + '\MwmBuilder\Content\\'
    outFile = outDir + os.path.basename(path)
    createDir(outDir)
    tree.write(outFile, 'utf-8', True)
    # copy stats so Mwm Builder knows if xml was updated
    shutil.copystat(path, outFile)

    # find L.O.D. models
    for Model in root.findall('./LOD/Model'):
        primary_mwm = path[:-3].replace(startDir + '\\', '') + 'mwm'
        file = Model.text
        if not Model.text.endswith('.mwm'):
            file += '.mwm'

        try:
            if model_size[(primary_mwm).lower()] == 'Large':
                toCopy_modelLarge[(module + '\\' + file).lower()] = path
            else:
                toCopy_modelSmall[(module + '\\' + file).lower()] = path
        except KeyError:
            errors.append('ERROR: Cannot find main file for:\n\t' + file + '\nnot in data files:\n\t' + primary_mwm)
    return True


def copyHavokFile(path):
	model = path.replace(startDir +'\\', '').lower()[:-4]
	my_havok = model + '.hkt'
	
	if not os.path.exists(my_havok):
		try:
			pri_havok = primary_model[model] + '.hkt'
			if not os.path.exists(pri_havok):
				warning.append('WARN: no havok file for ' + path)
				return
		except KeyError:
			warning.append('WARN: no havok file for ' + path)
			return
	else:
		pri_havok = my_havok
	
	outDir = os.path.dirname(model) + '\MwmBuilder\Content\\'
	outFile = outDir + os.path.basename(my_havok)
	createDir(outDir)
	shutil.copy2(pri_havok, outFile)
	

# find and parse all the .xml files, then start mwm builder
def find_xml(path):
    # find all the directories that contain xml files
    xmlDir = set()
    for root, dirs, files, in os.walk(path):
        if ('MwmBuilder\Content' in root):
            continue
        for file in files:
            if (file.endswith('.xml')):
                xmlDir.add(root)

    # for each dir in xmlDir, set up the mwmBuilder
    for dir in xmlDir:
        if os.path.exists(dir):
            eraseDir(dir + '\\' + 'MwmBuilder')
            print ('dir: '+dir)
            for file in os.listdir(dir):
                if (file.endswith('.xml')):
                    path = dir + '\\' + file
                    if (parse_xml(path)):
                        copyHavokFile(path)
                   
            # run build-model
            if os.path.exists(mwmBuilder):
                os.chdir(dir)
                mwmProcess.append(subprocess.Popen(["python", build_model]))


# method that takes a module name and moves the files
def copyFiles(l_source):
    info.append("copying scripts from "+l_source)
    l_sourceDir = startDir + "\\" + l_source + "\Scripts"
    l_dataDir = startDir + "\\" + l_source + "\Data"
    l_archiveDir = startDir + "\Archive\\" + l_source

    createDir(l_archiveDir)
    ignoreDirs = [ "bin", "obj", "Properties" ] # these are case-sensitive

    for path, dirs, files in os.walk(l_sourceDir):
        for ignore in ignoreDirs:
            if (ignore in dirs):
                dirs.remove(ignore)

        os.chdir(path)

        nsPath = path.replace(l_sourceDir,'')
        nsStr = nsPath.replace("\\","") + '.' if nsPath != '' else ''

        for file in files:
            if not file.lower().endswith(".cs"):
                continue

            #print ("file is "+file)
            lines = open(file, 'r').readlines()

            if (len(lines) == 0 or  "skip file on build" in lines[0]):
                #print ("skipping "+file)
                continue

            l_destFileName =  l_source + '.' + nsStr + file
            l_destFile = finalScript + l_destFileName
            l_destFileDev = finalScriptDev + l_destFileName

            # fake the pre-processor
            # remove symbols so scripts will compile
            # remove Conditional in Dev version
            # compiler will still remove Conditional statements in released version
            destFile = open(l_destFile, 'w')
            destFileDev = open(l_destFileDev, 'w')
            for line in lines:
                if (not line.lstrip().startswith("//")):
                    if ("#define LOG_ENABLED" in line): # could not make startswith work
                        destFile.write("// pre-processor symbol removed by build.py\n")
                        destFileDev.write("// pre-processor symbol removed by build.py\n")
                        continue
                    if ("System.Diagnostics.Conditional" in line):
                        destFile.write(line)
                        destFileDev.write("// Conditional removed by build.py\n")
                        continue
                destFile.write(line)
                destFileDev.write(line)

            # for archive, add date and time to file name
            d = datetime.datetime.fromtimestamp(os.path.getmtime(file))
            formated = str(d.year)+"-"+str(d.month).zfill(2)+"-"+str(d.day).zfill(2)+"_"+str(d.hour).zfill(2)+"-"+str(d.minute).zfill(2)+"_"+file
            archive = l_archiveDir +"\\"+formated
            try:
                os.chmod(archive, stat.S_IWRITE)
            except OSError:
                pass
            shutil.copyfile(file, archive)
            os.chmod(archive, stat.S_IREAD)


def build_help():
    replaceIn_file=r"Help.cs"
    replaceIn_DirSource= startDir + r"\Autopilot\Scripts"

    readme_file= startDir + r"\Steam Description\Autopilot Navigation.txt"

    replaceIn_write_name = finalScript+"\\Autopilot."+replaceIn_file
    replaceIn_writeDev_name = finalScriptDev+"\\Autopilot."+replaceIn_file

    replaceIn_read = open(replaceIn_DirSource+"\\"+replaceIn_file, 'r')
    replaceIn_write = open(replaceIn_write_name+".tmp", 'w')
    replaceIn_writeDev = open(replaceIn_writeDev_name+".tmp", 'w')
    readme_read = open(readme_file, 'r')

    #    get all commands
    readme_lines = readme_read.readlines()
    command_lines = []
    command_current = ""
    index = 0
    position = 0
    while (position == 0):
        if ("[h1]Commands[/h1]" in readme_lines[index]):
            position+=1
        else:
            index+=1
    while (0==0):
        index+=1
        line_cur = readme_lines[index]

        if ("Advanced Commands" in line_cur):
            continue
        if ("\n" is line_cur):
            continue
        if ("[h1]" in line_cur):
            if ("[/h1]" in line_cur):
                if (command_current):
                    command_lines.append(command_current) # append current
                break

        line_cur = line_cur.replace("\"", "\"\"")

        if ("Example - " in line_cur):
            command_current += line_cur # attach to current
        else:
            if (command_current):
                command_lines.append(command_current) # append current
            command_current = line_cur # start new command

    #    copy all commands
    for line in replaceIn_read.readlines():
        if ("fill commands by build" in line):
            # write from commands
            for command_line in command_lines:
                replaceIn_write.write("allCommands.Add(new Command(@\""+command_line+"\"));\n")
                replaceIn_writeDev.write("allCommands.Add(new Command(@\""+command_line+"\"));\n")
        else:
            replaceIn_write.write(line)
            replaceIn_writeDev.write(line)

    replaceIn_write.close()
    replaceIn_writeDev.close()

    os.rename(replaceIn_write_name+".tmp", replaceIn_write_name)
    #print("wrote: "+replaceIn_file)
    os.rename(replaceIn_writeDev_name+".tmp", replaceIn_writeDev_name)
    #print("wrote(Dev): "+replaceIn_file)


def copyToFinal(fileMap, finalRelDir):
    finalModDir = finalDir +'\\' + finalRelDir
    finalModDirDev = finalDirDev +'\\' + finalRelDir
    createDir(finalModDir)
    createDir(finalModDirDev)

    for file in fileMap.keys():
        #print ('copying file: ' + file + ', to ' + finalModDir)
        try:
            shutil.copy2(startDir + '\\' + file, finalModDir)
            shutil.copy2(startDir + '\\' + file, finalModDirDev)
        except (FileNotFoundError, OSError):
            errors.append('ERROR: the file: ' + file + '\nreferenced by:\n\t' + fileMap[file] + '\ncould not be found.')


def copyWithExtension(l_from, l_to, l_ext):
    createDir(l_to)
    l_ext = l_ext.lower()
    for file in os.listdir(l_from):
        if file.lower().endswith(l_ext):
            shutil.copy2(l_from + '\\' + file, l_to)


print ('\n\n')

if not os.path.exists(buildIni):
	shutil.copy(buildIniTemplate, buildIni)

if os.path.exists(buildIni):
    exec(open(buildIni).read())
else:
    print ('build.ini not found')
    investigateBadPath("build.ini", buildIni)

# erase old data
for file in os.listdir(finalDir):
    path = finalDir + '\\' + file
    if (os.path.isdir(path)):
        eraseDir(path)
for file in os.listdir(finalDirDev):
    path = finalDirDev + '\\' + file
    if (os.path.isdir(path)):
        eraseDir(path)

# get modules
os.chdir(startDir)
for file in os.listdir(startDir):
    if file[0] == '.' or file == "Archive":
        continue
    if os.path.isdir(file):
        modules.append(file)

# notify about Mwm Builder
if not os.path.exists(mwmBuilder):
    print ('Not building models')
    investigateBadPath("MwmBuilder", mwmBuilder)
    print()

# process data files
for module in modules[:]:
    dataPath = startDir + '\\' + module + '\Data'
    if os.path.exists(dataPath):
        find_sbc(dataPath)

# process xml files and models
for module in modules[:]:
    find_xml(startDir + '\\' + module)

print()

# build scripts
createDir(finalScript)
createDir(finalScriptDev)
for module in modules[:]:
    copyFiles(module)

# build help.cs
build_help()

# wait on mwmBuilder
if os.path.exists(mwmBuilder):
    for process in mwmProcess[:]:
        process.wait()

    print("\nfinished MwmBuilder\n")
    
# print info
for message in info:
    print (message)

# copy panel textures
for module in modules[:]:
    relDir = '\Textures\Models'
    textureDir = startDir + '\\' + module + relDir
    if os.path.exists(textureDir):
        copyWithExtension(textureDir, finalDir + relDir, ".dds")
        copyWithExtension(textureDir, finalDirDev + relDir, ".dds")

# copy files from collected paths
print('\ncopying files found in .sbc and .xml')
copyToFinal(toCopy_icons, finalRelDir_icons)
copyToFinal(toCopy_modelLarge, finalRelDir_model + '\Large')
copyToFinal(toCopy_modelSmall, finalRelDir_model + '\Small')
copyToFinal(toCopy_texture, finalRelDir_texture)
copyToFinal(toCopy_texturePanel, finalRelDir_texturePanel)

# print errors & warnings
print ('\n\nbuild finished with '+ str(len(errors)) + ' errors and ' + str(len(warning)) + ' warnings:')
for oneWarn in warning:
	print ('\n'+oneWarn)
for oneError in errors:
	print ('\n'+oneError)

#    Pack Archive

os.chdir(startDir)

if not os.path.exists(Zip7):
	print('\nNot running 7-Zip')
	sys.exit()

print("\n7-Zip running")

cmd = [Zip7, 'u', 'Archive.7z', 'Archive']
process = subprocess.Popen(cmd, stdout = open(os.devnull, 'wb'))
process.wait()

if process.returncode != 0:
    print("\n7-Zip failed\n")
    sys.exit()

print("7-Zip finished\n")

# copied from http://stackoverflow.com/questions/1213706/what-user-do-python-scripts-run-as-in-windows/1214935#1214935
def handleRemoveReadonly(func, path, exc):
  excvalue = exc[1]
  if func in (os.rmdir, os.remove) and excvalue.errno == errno.EACCES:
      os.chmod(path, stat.S_IRWXU| stat.S_IRWXG| stat.S_IRWXO) # 0777
      func(path)
  else:
      raise

shutil.rmtree('Archive', ignore_errors=False, onerror=handleRemoveReadonly)
