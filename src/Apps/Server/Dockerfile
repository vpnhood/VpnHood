# The image is a native Linux install, made by the release's own install script, and vhsupervisor
# does for it what systemd does for a native install (see vhsupervisor.sh). The build context is not
# the source but a folder publish_docker.ps1 stages: install-msquic.sh and vhsupervisor.sh, and per
# Docker architecture (amd64, arm64) the release's package and install script, as package.tar.gz and
# install.sh. It is bind-mounted rather than copied, so no package stays behind in a layer. The
# install runs with -container, as vhupdate runs it here later: no units (no systemd here), the
# server's libraries brought current, and no storage folder - the server or a mount makes it.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
ARG TARGETARCH
RUN --mount=type=bind,target=/context \
	apt-get update \
	&& apt-get install -y --no-install-recommends wget \
	&& (bash /context/install-msquic.sh || true) \
	&& bash /context/${TARGETARCH}/install.sh -q -container -destination /app -packageFile /context/${TARGETARCH}/package.tar.gz \
	&& install -m 755 /context/vhsupervisor.sh /app/vhsupervisor \
	&& rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["/app/vhsupervisor"]
