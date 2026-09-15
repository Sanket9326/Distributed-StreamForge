#!lua flags=allow-oom
-- @common
expect(KEYS[1], 'string')
return redis.call('DEL', KEYS[1])
