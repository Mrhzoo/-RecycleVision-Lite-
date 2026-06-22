from importlib import metadata


class DistributionNotFound(Exception):
    pass


class Distribution:
    def __init__(self, version):
        self.version = version


def get_distribution(name):
    try:
        version = metadata.version(name)
    except metadata.PackageNotFoundError as exc:
        raise DistributionNotFound from exc
    return Distribution(version)
