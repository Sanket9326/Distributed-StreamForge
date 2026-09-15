local function expect(key, kind)
  local actual = redis.call('TYPE', key).ok
  if actual ~= 'none' and actual ~= kind then error('INVALID_KEY_TYPE') end
end
local function decimal(value)
  if type(value) ~= 'string' or not string.match(value, '^%d+$') then error('INVALID_INTEGER') end
  local normalized = string.gsub(value, '^0+', '')
  return normalized == '' and '0' or normalized
end
local function compare(a, b)
  a = decimal(a); b = decimal(b)
  if #a ~= #b then return #a < #b and -1 or 1 end
  if a == b then return 0 end
  return a < b and -1 or 1
end
