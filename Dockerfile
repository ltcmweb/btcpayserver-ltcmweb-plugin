FROM golang:1.24
RUN go install github.com/ltcmweb/mwebd/cmd/mwebd@latest
EXPOSE 12345
ENTRYPOINT ["/go/bin/mwebd", "-d", "/data", "-l", ":12345"]
