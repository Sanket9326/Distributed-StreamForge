#!lua flags=no-writes
-- @common
expect(KEYS[1], 'string')
return redis.call('GET', KEYS[1])
