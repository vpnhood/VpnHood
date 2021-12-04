$solutionDir = Split-Path -parent $PSScriptRoot;
$publishProfile = Join-Path -Path $solutionDir  -ChildPath "Properties\PublishProfiles\VhAccessServer - ZipDeploy.pubxml"

# commit and push git
$gitDir = "$solutionDir/.git";
git --git-dir=$gitDir --work-tree=$solutionDir commit -a -m "Publishing";
git --git-dir=$gitDir --work-tree=$solutionDir pull;
git --git-dir=$gitDir --work-tree=$solutionDir push;

# swtich to main branch
git --git-dir=$gitDir --work-tree=$solutionDir checkout master
git --git-dir=$gitDir --work-tree=$solutionDir pull;
git --git-dir=$gitDir --work-tree=$solutionDir merge development;
git --git-dir=$gitDir --work-tree=$solutionDir push;
git --git-dir=$gitDir --work-tree=$solutionDir checkout development
