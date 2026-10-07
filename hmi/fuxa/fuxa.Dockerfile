# FUXA with its Modbus driver. The frangoteam/fuxa image ships without the
# modbus-serial package: FUXA installs it as a plugin, from npm, at run time.
# Installing it here, at the version FUXA's plugin registry names, makes the
# stack start the same way every time, with no download after the build.

FROM frangoteam/fuxa:1.3.4@sha256:3778da3377e7685842495497d13157d1548ccad8708c434dc5d4b99fb5cb25bf
WORKDIR /usr/src/app/FUXA/server
RUN npm install --no-audit --no-fund --save-exact modbus-serial@8.0.19
