# Litecoin MWEB support plugin

This plugin extends BTCPay Server to enable users to receive payments via Litecoin MWEB.

## Configuration

Configure this plugin using the following environment variables:

| Environment variable | Description                                                                                                                                                                                                                                   | Example |
| --- |-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------| --- |
**BTCPAY_LTC_MWEB_DAEMON_URI** | **Required**. The URI of the [mwebd](https://github.com/ltcmweb/mwebd) RPC interface.                                                                                                                                               | http://127.0.0.1:12345 |

BTCPay Server's Docker deployment simplifies the setup by automatically configuring these variables.

# For maintainers

If you are a developer maintaining this plugin, in order to maintain this plugin, you need to clone this repository with `--recurse-submodules`:
```bash
git clone --recurse-submodules https://github.com/ltcmweb/btcpayserver-ltcmweb-plugin
```
Then run the tests dependencies
```bash
docker-compose up -d dev
```

Then create the `appsettings.dev.json` file in `btcpayserver/BTCPayServer`, with the following content:

```json
{
  "DEBUG_PLUGINS": "..\\..\\Plugins\\LitecoinMweb\\bin\\Debug\\net8.0\\BTCPayServer.Plugins.LitecoinMweb.dll",
  "LTC_MWEB_DAEMON_URI": "http://127.0.0.1:12345"
}
```
This will ensure that BTCPay Server loads the plugin when it starts.

Then start the development dependencies via docker-compose:
```bash
docker-compose up -d dev
```

Finally, set up BTCPay Server as the startup project in [Rider](https://www.jetbrains.com/rider/) or Visual Studio.

If you want to reset the environment you can run:
```bash
docker-compose down -v
docker-compose up -d dev
```

Note: Running or compiling the BTCPay Server project will not automatically recompile the plugin project. Therefore, if you make any changes to the project, do not forget to build it before running BTCPay Server in debug mode.

We recommend using [Rider](https://www.jetbrains.com/rider/) for plugin development, as it supports hot reload with plugins. You can edit `.cshtml` files, save, and refresh the page to see the changes.

Visual Studio does not support this feature.

## About docker-compose deployment

BTCPay Server maintains its own [deployment stack project](https://github.com/btcpayserver/btcpayserver-docker) to enable users to easily update or deploy additional infrastructure (such as nodes).

Litecoin nodes are defined in this [Docker Compose file](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docker-compose-generator/docker-fragments/litecoin.yml).

The Litecoin images are also maintained in the [dockerfile-deps repository](https://github.com/btcpayserver/dockerfile-deps/tree/master/Litecoin). While using the `dockerfile-deps` for future versions of Litecoin Dockerfiles is optional, maintaining [the Docker Compose Fragment](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docker-compose-generator/docker-fragments/litecoin.yml) is necessary.

Users can install Litecoin by configuring the `BTCPAYGEN_CRYPTOX` environment variables.

For example, after ensuring `BTCPAYGEN_CRYPTO2` is not already assigned to another cryptocurrency:
```bash
BTCPAYGEN_CRYPTO2="ltc"
. btcpay-setup.sh -i
```

This will automatically configure Litecoin in their deployment stack. Users can then run `btcpay-update.sh` to pull updates for the infrastructure.

Note: Adding Litecoin to the infrastructure is not recommended for non-advanced users. If the server specifications are insufficient, it may become unresponsive.

Lunanode, a VPS provider, offers an [easy way to provision the infrastructure](https://docs.btcpayserver.org/Deployment/LunaNode/) for BTCPay Server, then it installs the Docker Compose deployment on the provisioned VPS. The user can select Litecoin during provisioning, then the resulting VPS have a Litecoin deployed automatically, without the need for the user to use the command line. (But the user will still need to install this plugin manually)

# Licence

[MIT](LICENSE.md)
